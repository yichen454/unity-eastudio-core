using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class EnableFlow : MonoBehaviour
{
    [Tooltip("需要依次激活的对象")]
    public GameObject[] flowsToEnable;

    [Tooltip("单帧激活时间预算(ms)")]
    public float frameBudgetMs = 40f;

    private List<GameObject> _activeFlows = new List<GameObject>();

    private void Awake()
    {
        foreach (var flow in flowsToEnable)
        {
            if (flow != null && flow.activeSelf)
            {
                flow.SetActive(false);
                _activeFlows.Add(flow);
            }
        }
    }

    private void Start()
    {
        StartCoroutine(Flow());
    }

    private IEnumerator Flow()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame(); // 等待一帧，确保所有对象都已禁用
        float frameStartTime = Time.realtimeSinceStartup;
        foreach (var flow in _activeFlows)
        {
            if (flow == null)
                continue;

            flow.SetActive(true);

            float elapsedMs =
                (Time.realtimeSinceStartup - frameStartTime) * 1000f;

            if (elapsedMs >= frameBudgetMs)
            {
                yield return new WaitForEndOfFrame();

                frameStartTime = Time.realtimeSinceStartup;
            }
        }
    }
}