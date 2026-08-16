using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class DisableRendererCulling : MonoBehaviour
{
    [Header("Bounds Size")]
    public Vector3 boundsSize = Vector3.one * 10000f;

    [Header("Bounds Center Offset")]
    public Vector3 boundsCenterOffset = Vector3.zero;

    [Header("Apply Every Frame")]
    public bool updateEveryFrame = true;

    private Renderer rend;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        ApplyBounds();
    }

    void LateUpdate()
    {
        if (updateEveryFrame)
        {
            ApplyBounds();
        }
    }

    void ApplyBounds()
    {
        Bounds hugeBounds = new Bounds(
            boundsCenterOffset,
            boundsSize
        );

        // SRP / 新版Unity推荐
        rend.localBounds = hugeBounds;

        // SkinnedMesh 特殊处理
        SkinnedMeshRenderer smr = rend as SkinnedMeshRenderer;
        if (smr != null)
        {
            smr.updateWhenOffscreen = true;
        }
    }
}