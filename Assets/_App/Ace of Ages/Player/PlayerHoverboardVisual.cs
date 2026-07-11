using UnityEngine;

/// <summary>
/// Main-scene hoverboard visual that follows the ECS player follow object and tilts to match
/// terrain normal. XR rig stays upright; only this visual tilts.
/// </summary>
[AddComponentMenu("Ace of Ages/Player Hoverboard Visual")]
[DefaultExecutionOrder(110)]
public class PlayerHoverboardVisual : MonoBehaviour
{
    private const float MinWalkableNormalY = 0.01f;

    [Tooltip("Transform that receives pitch/roll tilt (HIps mount). Defaults to this transform.")]
    [SerializeField] private Transform hipsMount;

    [Tooltip("Seconds to smooth pitch/roll toward the target tilt. 0 = instant snap.")]
    [SerializeField] private float tiltSmoothTime = 0.12f;

    [Tooltip("Height above the follow position where the ground ray starts.")]
    [SerializeField] private float rayHeightAbove = 50f;

    [Tooltip("Max ray length below the follow position.")]
    [SerializeField] private float rayLengthBelow = 50f;

    [Tooltip("Max distance above the terrain surface to still align tilt.")]
    [SerializeField] private float maxTiltDistance = 8f;

    [Tooltip("Physics layers treated as terrain for tilt raycasts.")]
    [SerializeField] private LayerMask terrainLayers = 1 << 11;

    [Header("Head Roll Yaw")]
    [Tooltip("Board Y rotation = HMD roll × this multiplier.")]
    [SerializeField] private float headYawMultiplier = 2f;

    [Tooltip("Maximum board Y rotation from head roll (degrees).")]
    [SerializeField] private float maxHeadYaw = 90f;

    [Header("Head Roll Z")]
    [Tooltip("Hoverboard mesh that receives head-roll banking (e.g. SM_Veh_Hoverboard_01).")]
    [SerializeField] private Transform boardVisual;

    [Tooltip("Board Z rotation = HMD roll × this multiplier.")]
    [SerializeField] private float headRollMultiplier = 1f;

    [Tooltip("Maximum board Z rotation from head roll (degrees).")]
    [SerializeField] private float maxHeadRoll = 45f;

    private Quaternion _smoothedLocalRotation = Quaternion.identity;
    private Quaternion _smoothedBoardLocalRotation = Quaternion.identity;

    private void Awake()
    {
        if (hipsMount == null)
            hipsMount = transform;

        if (boardVisual == null && hipsMount != null && hipsMount.childCount == 1)
            boardVisual = hipsMount.GetChild(0);
    }

    private void LateUpdate()
    {
        if (!PlayerFollowObjectPoseBridge.IsValid)
            return;

        Vector3 followPosition = PlayerFollowObjectPoseBridge.Position;
        Quaternion followRotation = PlayerFollowObjectPoseBridge.Rotation;

        transform.SetPositionAndRotation(followPosition, followRotation);

        Vector3 terrainNormal = Vector3.up;
        bool useTerrainNormal = false;

        if (PlayerFollowObjectPoseBridge.HasTiltTerrainNormal)
        {
            terrainNormal = PlayerFollowObjectPoseBridge.TerrainNormal;
            useTerrainNormal = true;
        }
        else if (TryGetTerrainNormal(followPosition, out Vector3 raycastNormal))
        {
            terrainNormal = raycastNormal;
            useTerrainNormal = true;
        }

        Quaternion targetLocal = useTerrainNormal
            ? ComputeTerrainAlignedLocalRotation(followRotation, terrainNormal)
            : Quaternion.identity;

        float headBank = GetHeadBankAngle();
        float boardYaw = Mathf.Clamp(-headBank * headYawMultiplier, -maxHeadYaw, maxHeadYaw);
        targetLocal *= Quaternion.Euler(0f, boardYaw, 0f);

        if (tiltSmoothTime <= 0f)
        {
            _smoothedLocalRotation = targetLocal;
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime / tiltSmoothTime);
            _smoothedLocalRotation = Quaternion.Slerp(_smoothedLocalRotation, targetLocal, t);
        }

        hipsMount.localRotation = _smoothedLocalRotation;

        if (boardVisual != null)
        {
            float boardRoll = Mathf.Clamp(headBank * headRollMultiplier, -maxHeadRoll, maxHeadRoll);
            Quaternion targetBoardLocal = Quaternion.Euler(0f, 0f, boardRoll);

            if (tiltSmoothTime <= 0f)
            {
                _smoothedBoardLocalRotation = targetBoardLocal;
            }
            else
            {
                float t = Mathf.Clamp01(Time.deltaTime / tiltSmoothTime);
                _smoothedBoardLocalRotation = Quaternion.Slerp(_smoothedBoardLocalRotation, targetBoardLocal, t);
            }

            boardVisual.localRotation = _smoothedBoardLocalRotation;
        }
    }

    private bool TryGetTerrainNormal(Vector3 position, out Vector3 terrainNormal)
    {
        terrainNormal = Vector3.up;

        Vector3 rayStart = position + Vector3.up * rayHeightAbove;
        float maxDistance = rayHeightAbove + rayLengthBelow;

        if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxDistance, terrainLayers, QueryTriggerInteraction.Ignore))
            return false;

        terrainNormal = hit.normal;
        if (terrainNormal.y < MinWalkableNormalY)
            return false;

        float heightAboveSurface = Vector3.Dot(position - hit.point, terrainNormal);
        return heightAboveSurface <= maxTiltDistance;
    }

    private static Quaternion ComputeTerrainAlignedLocalRotation(Quaternion parentRotation, Vector3 groundNormal)
    {
        Vector3 up = groundNormal.sqrMagnitude > 0.0001f ? groundNormal.normalized : Vector3.up;
        Vector3 forward = parentRotation * Vector3.forward;
        forward = Vector3.ProjectOnPlane(forward, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(parentRotation * Vector3.right, up);

        forward.Normalize();
        Quaternion targetWorld = Quaternion.LookRotation(forward, up);
        return Quaternion.Inverse(parentRotation) * targetWorld;
    }

    private static float GetHeadBankAngle()
    {
        if (Camera.main == null)
            return 0f;

        float z = Camera.main.transform.eulerAngles.z;
        return z > 180f ? z - 360f : z;
    }
}
