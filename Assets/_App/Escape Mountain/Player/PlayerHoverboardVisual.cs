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

    [Header("Terrain Normal Smoothing")]
    [Tooltip("Seconds to smooth the terrain normal across mesh facets. 0 = instant (raw normal).")]
    [SerializeField] private float normalSmoothTime = 0.12f;

    [Header("Head Roll Yaw")]
    [Tooltip("Board Y rotation = HMD roll × this multiplier.")]
    [SerializeField] private float headYawMultiplier = 2f;

    [Tooltip("Maximum board Y rotation from head roll (degrees).")]
    [SerializeField] private float maxHeadYaw = 90f;

    [Tooltip("Meters to push Hips (and the board under it) forward at full yaw. 0 = disabled.")]
    [SerializeField] private float hipsTurnForwardOffset = 0.25f;

    [Header("Head Roll Z")]
    [Tooltip("Hoverboard mesh that receives head-roll banking (e.g. SM_Veh_Hoverboard_01).")]
    [SerializeField] private Transform boardVisual;

    [Tooltip("Board Z rotation = HMD roll × this multiplier.")]
    [SerializeField] private float headRollMultiplier = 1f;

    [Tooltip("Maximum board Z rotation from head roll (degrees).")]
    [SerializeField] private float maxHeadRoll = 45f;

    private Quaternion _smoothedLocalRotation = Quaternion.identity;
    private Quaternion _smoothedBoardLocalRotation = Quaternion.identity;
    private Vector3 _baseHipsLocalPosition;
    private Vector3 _smoothedHipsLocalPosition;

    private Vector3 _smoothedTerrainNormal = Vector3.up;
    private bool _hasInitializedNormal;

    private void Awake()
    {
        if (hipsMount == null)
            hipsMount = transform;

        if (boardVisual == null && hipsMount != null && hipsMount.childCount == 1)
            boardVisual = hipsMount.GetChild(0);

        _baseHipsLocalPosition = hipsMount.localPosition;
        _smoothedHipsLocalPosition = _baseHipsLocalPosition;
    }

    private void OnEnable()
    {
        _hasInitializedNormal = false;
    }

    private void LateUpdate()
    {
        if (!PlayerFollowObjectPoseBridge.IsValid)
            return;

        Quaternion followRotation = PlayerFollowObjectPoseBridge.Rotation;

        // The board sits on the Terrain contact under the sliding sphere. In flight there is no
        // contact point, so the board travels with the rider.
        Vector3 boardPosition = PlayerFollowObjectPoseBridge.HasBoardContact
            ? PlayerFollowObjectPoseBridge.BoardContactPosition
            : PlayerFollowObjectPoseBridge.Position;

        transform.SetPositionAndRotation(boardPosition, followRotation);

        Vector3 targetTerrainNormal = Vector3.up;

        if (PlayerFollowObjectPoseBridge.HasTiltTerrainNormal)
        {
            targetTerrainNormal = PlayerFollowObjectPoseBridge.TerrainNormal;
        }
        else if (TryGetTerrainNormal(boardPosition, out Vector3 raycastNormal))
        {
            targetTerrainNormal = raycastNormal;
        }

        if (!_hasInitializedNormal)
        {
            _smoothedTerrainNormal = targetTerrainNormal;
            _hasInitializedNormal = true;
        }
        else if (normalSmoothTime <= 0f)
        {
            _smoothedTerrainNormal = targetTerrainNormal;
        }
        else
        {
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, normalSmoothTime));
            _smoothedTerrainNormal = Vector3.Slerp(_smoothedTerrainNormal, targetTerrainNormal, t);
        }

        Quaternion targetLocal = ComputeTerrainAlignedLocalRotation(followRotation, _smoothedTerrainNormal);

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

        float turnFactor = maxHeadYaw > 0f ? Mathf.Clamp01(Mathf.Abs(boardYaw) / maxHeadYaw) : 0f;
        Vector3 targetHipsLocal = _baseHipsLocalPosition + Vector3.forward * (turnFactor * hipsTurnForwardOffset);

        if (tiltSmoothTime <= 0f)
        {
            _smoothedHipsLocalPosition = targetHipsLocal;
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime / tiltSmoothTime);
            _smoothedHipsLocalPosition = Vector3.Lerp(_smoothedHipsLocalPosition, targetHipsLocal, t);
        }

        hipsMount.localPosition = _smoothedHipsLocalPosition;

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

        // Pitch only: drop the sideways component of the terrain normal so side slopes do not
        // roll the board around Z. Fore/aft slope still tilts the nose up/down.
        Vector3 right = parentRotation * Vector3.right;
        Vector3 pitchUp = Vector3.ProjectOnPlane(up, right);
        if (pitchUp.sqrMagnitude < 0.0001f)
            return Quaternion.identity;
        pitchUp.Normalize();

        Vector3 forward = Vector3.ProjectOnPlane(parentRotation * Vector3.forward, pitchUp);
        if (forward.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        forward.Normalize();
        Quaternion targetWorld = Quaternion.LookRotation(forward, pitchUp);
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
