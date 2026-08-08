using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Shows a hit ring at the XR ray endpoint for UI Toolkit / 3D UI hits.
/// Unlike <see cref="UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual"/>'s
/// reticle (valid interactable targets only), this uses <see cref="XRRayInteractor.TryGetCurrentRaycast"/>
/// so UITK world-space panel colliders get a ring.
/// </summary>
[DisallowMultipleComponent]
public class UiRayHitRingVisual : MonoBehaviour
{
    const float SurfaceOffset = 0.002f;

    [SerializeField] GameObject ringPrefab;
    [SerializeField] float ringScale = 0.012f;
    [SerializeField] int handLayer = -1;

    XRRayInteractor rayInteractor;
    GameObject ringInstance;
    Transform ringTransform;

    public void Initialize(GameObject prefab, float scale, int layer)
    {
        ringPrefab = prefab;
        ringScale = scale;
        handLayer = layer;
        EnsureRingInstance();
    }

    void Awake()
    {
        rayInteractor = GetComponent<XRRayInteractor>();
        EnsureRingInstance();
    }

    void OnDisable()
    {
        if (ringInstance != null)
            ringInstance.SetActive(false);
    }

    void OnDestroy()
    {
        if (ringInstance != null)
            Destroy(ringInstance);
    }

    void LateUpdate()
    {
        if (rayInteractor == null || ringInstance == null)
            return;

        if (!TryGetHit(out Vector3 position, out Vector3 normal))
        {
            ringInstance.SetActive(false);
            return;
        }

        // Match AutoHand HandCanvasPointer: sit slightly off the surface, face along -normal.
        ringTransform.SetPositionAndRotation(
            position + normal * SurfaceOffset,
            Quaternion.LookRotation(-normal));
        ringTransform.localScale = Vector3.one * ringScale;
        ringInstance.SetActive(true);
    }

    bool TryGetHit(out Vector3 position, out Vector3 normal)
    {
        position = default;
        normal = default;

        if (!rayInteractor.TryGetCurrentRaycast(
                out RaycastHit? raycastHit,
                out _,
                out UnityEngine.EventSystems.RaycastResult? uiRaycastHit,
                out _,
                out bool isUIHitClosest))
            return false;

        if (isUIHitClosest && uiRaycastHit.HasValue)
        {
            var hit = uiRaycastHit.Value;
            position = hit.worldPosition;
            normal = hit.worldNormal;
            if (Vector3.Dot(rayInteractor.rayOriginTransform.forward, normal) > 0f)
                normal = -normal;
            return true;
        }

        if (raycastHit.HasValue && raycastHit.Value.collider != null)
        {
            var hit = raycastHit.Value;
            position = hit.point;
            normal = hit.normal;
            if (normal.sqrMagnitude < 0.0001f)
                normal = -rayInteractor.rayOriginTransform.forward;
            return true;
        }

        return false;
    }

    void EnsureRingInstance()
    {
        if (ringInstance != null || ringPrefab == null)
            return;

        ringInstance = Instantiate(ringPrefab);
        ringInstance.name = $"{ringPrefab.name} ({name})";
        ringTransform = ringInstance.transform;
        ringTransform.localScale = Vector3.one * ringScale;

        if (handLayer >= 0)
            SetLayerRecursive(ringTransform, handLayer);

        ringInstance.SetActive(false);
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }
}
