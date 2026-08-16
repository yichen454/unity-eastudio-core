using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using SceneObj = UnityEngine.SceneManagement.Scene;

namespace EA.Timeline
{
    /// <summary>
    /// 异步预加载、激活、卸载场景的 MonoBehaviour，可由 <see cref="EA.Scene.Timeline.SceneWarmupTrack"/> 驱动。
    /// <para>编辑模式下加载始终为同步 Additive，<see cref="loadMode"/> 仅在运行模式下生效。</para>
    /// </summary>
    public class SceneWarmup : MonoBehaviour
    {
        [Tooltip("目标场景资产。")]
        public SceneReference scene;

        [Tooltip(
            "加载模式（仅运行模式有效，编辑模式始终以 Additive 同步加载）：\n" +
            "• PreloadOnly     — 后台加载至 90% 后暂停，需手动调用 ActivateScene() 激活\n" +
            "• LoadAndActivate — 加载完成后立即激活\n" +
            "• LoadAndReplace  — 激活后卸载包含本组件的场景")]
        public SceneLoadMode loadMode = SceneLoadMode.LoadAndActivate;

        [Tooltip("加载完成后将目标场景设为活动场景（影响光照 / 音频）。PreloadOnly 模式请在 ActivateScene() 后手动调用 SetSceneActive()。")]
        public bool setActiveOnLoad = false;

        [Tooltip("Timeline clip 结束时自动调用 UnloadScene()；关闭则需外部手动卸载。")]
        public bool unloadOnClipEnd = true;

        [Tooltip("允许在编辑模式（非 Play Mode）下执行加载 / 卸载操作。默认关闭，避免意外修改场景。")]
        public bool runInEditMode = false;

        [Space]
        [Tooltip("PreloadOnly 模式下加载到达 90% 时触发（编辑模式下不会触发）。")]
        public UnityEvent onScenePreloaded = new UnityEvent();

        [Tooltip("场景完成加载并激活后触发。")]
        public SceneLoadedEvent onSceneLoaded = new SceneLoadedEvent();

        [Tooltip("ActivateScene() 完成激活后触发（仅 PreloadOnly 模式）。")]
        public SceneLoadedEvent onSceneActivated = new SceneLoadedEvent();

        [Tooltip("场景卸载完成后触发。")]
        public UnityEvent onSceneUnloaded = new UnityEvent();

        [Tooltip("Timeline clip 结束时触发。")]
        public UnityEvent onClipEnd = new UnityEvent();

        // 持有引用以便 ActivateScene() 稍后翻转 allowSceneActivation。
        private AsyncOperation _pendingLoad;

        private bool IsPreloadOnly => loadMode == SceneLoadMode.PreloadOnly;
        private bool IsReplaceMode => loadMode == SceneLoadMode.LoadAndReplace;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (IsPreloadOnly && setActiveOnLoad)
                Debug.LogWarning(
                    $"[SceneWarmup] '{name}': PreloadOnly 模式下 setActiveOnLoad 无效，" +
                    "请在 ActivateScene() 之后手动调用 SetSceneActive()。",
                    this);

            if (IsPreloadOnly || IsReplaceMode)
                Debug.LogWarning(
                    $"[SceneWarmup] '{name}': loadMode = {loadMode} 在编辑模式下不生效，" +
                    "编辑模式始终以 Additive 同步加载。",
                    this);
        }
#endif

        // ── 加载 / 预加载 ──────────────────────────────────────────────────────

        /// <summary>
        /// Additive 加载目标场景。PreloadOnly 模式下加载至 90% 后暂停，需调用 <see cref="ActivateScene"/> 完成激活。
        /// 编辑模式下始终同步 Additive 加载，<see cref="loadMode"/> 不生效。可安全多次调用。
        /// </summary>
        public void LoadScene()
        {
            if (!scene.IsValid) return;

            SceneObj loaded = SceneManager.GetSceneByPath(scene.ScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                ApplySetActiveScene(loaded);
                onSceneLoaded.Invoke(loaded);
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!runInEditMode) return;
                var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    scene.ScenePath,
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);
                ApplySetActiveScene(s);
                onSceneLoaded.Invoke(s);
                return;
            }
#endif
            if (_pendingLoad != null) return; // 已在预加载中
            StartCoroutine(LoadCoroutine());
        }

        /// <summary>
        /// 完成预加载场景的激活（仅 PreloadOnly 模式）。激活后触发 <see cref="onSceneActivated"/>。
        /// LoadAndReplace 模式下激活后卸载自身场景。场景未处于预加载状态时无效。
        /// </summary>
        public void ActivateScene()
        {
            if (_pendingLoad != null)
            {
                // LoadCoroutine 持有本地 op 引用；设置标志让其退出等待循环。
                _pendingLoad.allowSceneActivation = true;
                // 激活完成及回调均在 LoadCoroutine 内处理。
                return;
            }

            // 场景已完全加载 — 直接应用活动场景状态并触发回调。
            SceneObj loaded = SceneManager.GetSceneByPath(scene.ScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                ApplySetActiveScene(loaded);
                onSceneActivated.Invoke(loaded);
                if (IsReplaceMode)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        if (!runInEditMode) return;
                        SceneObj self = gameObject.scene;
                        if (self.IsValid() && self.isLoaded)
                            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(self, true);
                    }
                    else
#endif
                        StartCoroutine(UnloadCurrentScene());
                }
            }
        }

        // ── 卸载 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 卸载目标场景。若场景正在预加载，先完成激活再卸载。可安全多次调用。
        /// </summary>
        public void UnloadScene()
        {
            if (!scene.IsValid) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!runInEditMode) return;
                SceneObj editorScene = SceneManager.GetSceneByPath(scene.ScenePath);
                if (editorScene.IsValid() && editorScene.isLoaded)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(editorScene, true);
                    onSceneUnloaded.Invoke();
                }
                return;
            }
#endif
            if (_pendingLoad != null)
            {
                // 场景处于预加载中：激活它让 Unity 完成内存注册，
                // 随后在 isDone 后卸载 — 未注册的场景无法直接卸载。
                var pendingOp = _pendingLoad;
                _pendingLoad = null;
                pendingOp.allowSceneActivation = true;
                StartCoroutine(WaitThenUnload(pendingOp));
                return;
            }

            SceneObj loaded = SceneManager.GetSceneByPath(scene.ScenePath);
            if (!loaded.IsValid() || !loaded.isLoaded) return;

            StartCoroutine(UnloadCoroutine(loaded));
        }

        private IEnumerator WaitThenUnload(AsyncOperation pendingOp)
        {
            while (!pendingOp.isDone)
                yield return null;

            // 激活完成后立即隐藏根对象，避免出现一帧闪烁。
            SceneObj loaded = SceneManager.GetSceneByPath(scene.ScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                foreach (var root in loaded.GetRootGameObjects())
                    root.SetActive(false);

                yield return UnloadCoroutine(loaded);
            }
        }

        // ── 活动场景控制 ──────────────────────────────────────────────────────

        /// <summary>将目标场景设为 Unity 活动场景（影响光照 / 音频）。场景必须已加载。</summary>
        public void SetSceneActive()
        {
            SceneObj loaded = SceneManager.GetSceneByPath(scene.ScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(loaded);
                LightProbes.TetrahedralizeAsync();
            }
            else
                Debug.LogWarning($"[SceneWarmup] 无法激活 '{scene.SceneName}'：场景尚未加载。");
        }

        /// <summary>将活动场景恢复为包含本组件的场景。</summary>
        public void RestoreActiveScene()
        {
            SceneManager.SetActiveScene(gameObject.scene);
        }

        // ── 协程 ──────────────────────────────────────────────────────────────

        private IEnumerator LoadCoroutine()
        {
            // 使用本地引用，防止外部清除 _pendingLoad 时引发 NullRef。
            var op = SceneManager.LoadSceneAsync(scene.ScenePath, LoadSceneMode.Additive);
            _pendingLoad = op;
            if (op == null) yield break;

            if (IsPreloadOnly)
            {
                op.allowSceneActivation = false;

                while (op.progress < 0.9f)
                    yield return null;

                onScenePreloaded.Invoke();

                // 等待 ActivateScene() 或 UnloadScene() 翻转标志
                while (!op.allowSceneActivation)
                    yield return null;

                while (!op.isDone)
                    yield return null;
            }
            else
            {
                yield return op;
            }

            _pendingLoad = null;

            SceneObj loaded = SceneManager.GetSceneByPath(scene.ScenePath);
            ApplySetActiveScene(loaded);
            onSceneLoaded.Invoke(loaded);

            if (IsPreloadOnly)
            {
                // PreloadOnly：由 ActivateScene() 驱动至此，触发激活回调。
                onSceneActivated.Invoke(loaded);
                if (IsReplaceMode)
                    yield return UnloadCurrentScene();
            }
            else if (IsReplaceMode)
            {
                // LoadAndReplace：加载完成后立即卸载自身场景。
                yield return UnloadCurrentScene();
            }
        }

        public void UnloadCurrentSceneImmediate()
        {
            SceneObj self = gameObject.scene;
            if (!self.IsValid() || !self.isLoaded) return;

            SceneManager.UnloadSceneAsync(self);
        }

        /// <summary>卸载包含本组件的场景（延迟一帧让新场景完成初始化）。</summary>
        private IEnumerator UnloadCurrentScene()
        {
            SceneObj self = gameObject.scene;
            if (!self.IsValid() || !self.isLoaded) yield break;

            yield return null; // 等一帧，让新场景完成 Awake / Start。

            SceneManager.UnloadSceneAsync(self);
        }

        private IEnumerator UnloadCoroutine(SceneObj target)
        {
            AsyncOperation op = SceneManager.UnloadSceneAsync(target);
            if (op == null) yield break;

            yield return op;

            onSceneUnloaded.Invoke();
        }

        // setActiveOnLoad 仅在非 PreloadOnly 模式下应用。
        private void ApplySetActiveScene(SceneObj loaded)
        {
            if (setActiveOnLoad && !IsPreloadOnly && loaded.IsValid())
                SceneManager.SetActiveScene(loaded);
        }
    }

    /// <summary>场景加载模式。</summary>
    public enum SceneLoadMode
    {
        /// <summary>仅预加载：在后台加载至 90% 后暂停，需手动调用 ActivateScene() 激活。</summary>
        PreloadOnly,

        /// <summary>加载后激活：加载完成后立即激活场景。</summary>
        LoadAndActivate,

        /// <summary>加载后激活且替换当前场景：激活目标场景后卸载包含本组件的场景。</summary>
        LoadAndReplace,
    }

    /// <summary>传递已加载 <see cref="UnityEngine.SceneManagement.Scene"/> 的 UnityEvent。</summary>
    [Serializable]
    public class SceneLoadedEvent : UnityEvent<SceneObj> { }
}

