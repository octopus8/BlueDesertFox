using System.Threading;
using Autohand;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using Hand = Autohand.Hand;

/// <summary>
/// World-space race start countdown (3-2-1-GO!). Starts when both hand-hold Grabbables
/// are held, then plays each value with a quick fade-in and a slower top-anchored
/// shrink + fade-out. On GO!, applies a forward push (base + rearward hand-pull
/// boost along player local Z) and releases the holds.
/// In the Editor only, also auto-starts 3 seconds after the scene loads.
/// </summary>
public class RaceCountdown : MonoBehaviour
{
    static readonly string[] Steps = { "3", "2", "1", "GO!" };

    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private CanvasGroup canvasGroup;

    [SerializeField] private Grabbable leftHandHold;
    [SerializeField] private Grabbable rightHandHold;

    [SerializeField] private GrabHintVisibility leftGrabHint;
    [SerializeField] private GrabHintVisibility rightGrabHint;

    [SerializeField] private float fadeInSeconds = 0.12f;
    [SerializeField] private float holdBeforeFadeOutSeconds = 0.15f;
    [SerializeField] private float fadeOutSeconds = 0.7f;
    [SerializeField] private float stepDurationSeconds = 1f;

    [Tooltip("Forward speed (m/s) added to the player follow object when GO! finishes.")]
    [SerializeField] private float goForwardSpeed = 4f;

    [Tooltip("Scales average rearward hand speed along player local Z into extra m/s added on GO!.")]
    [SerializeField] private float goHandPullSpeedScale = 1f;

    [SerializeField] private Color digitColor = new Color(0.78f, 0.92f, 1f, 1f);
    [SerializeField] private Color goColor = new Color(0.45f, 1f, 0.2f, 1f);
    [SerializeField] private float outlineWidth = 0.3f;
    [SerializeField] private Color outlineColor = new Color(0.02f, 0.05f, 0.12f, 1f);

    [SerializeField] private UnityEvent onCountdownComplete;

    CancellationTokenSource _runCts;
    bool _started;
    EntityQuery _followObjectQuery;
    bool _hasFollowObjectQuery;

    // Controller follow-pose velocity (body.linearVelocity is zero while holding on Quest).
    Vector3 _leftControllerVelocity;
    Vector3 _rightControllerVelocity;
    Vector3 _leftFollowSamplePos;
    Vector3 _rightFollowSamplePos;
    float _controllerSampleTime;
    bool _hasLeftFollowSample;
    bool _hasRightFollowSample;

    void Awake()
    {
        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (canvasGroup == null && label != null)
            canvasGroup = label.GetComponent<CanvasGroup>();

        if (label != null)
        {
            label.outlineWidth = outlineWidth;
            label.outlineColor = outlineColor;
            label.raycastTarget = false;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    void OnEnable()
    {
        Subscribe(leftHandHold);
        Subscribe(rightHandHold);
        TryStartCountdown();
    }

    void Update()
    {
        if (_started)
            UpdateControllerVelocitySamples();
    }

#if UNITY_EDITOR
    void Start()
    {
        EditorAutoStartAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    async UniTaskVoid EditorAutoStartAsync(CancellationToken ct)
    {
        await UniTask.Delay(System.TimeSpan.FromSeconds(3), cancellationToken: ct);
        BeginCountdown();
    }
#endif

    void OnDisable()
    {
        Unsubscribe(leftHandHold);
        Unsubscribe(rightHandHold);
        DisposeFollowObjectQuery();
    }

    void OnDestroy()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;

        DisposeFollowObjectQuery();

        if (label != null)
            label.transform.DOKill();
        if (canvasGroup != null)
            canvasGroup.DOKill();
    }

    void Subscribe(Grabbable grabbable)
    {
        if (grabbable == null)
            return;
        grabbable.OnGrabEvent += OnGrabbed;
        grabbable.OnReleaseEvent += OnReleased;
    }

    void Unsubscribe(Grabbable grabbable)
    {
        if (grabbable == null)
            return;
        grabbable.OnGrabEvent -= OnGrabbed;
        grabbable.OnReleaseEvent -= OnReleased;
    }

    void OnGrabbed(Hand hand, Grabbable grab) => TryStartCountdown();

    void OnReleased(Hand hand, Grabbable grab) { }

    void TryStartCountdown()
    {
        if (_started || leftHandHold == null || rightHandHold == null)
            return;
        if (!leftHandHold.IsHeld() || !rightHandHold.IsHeld())
            return;

        BeginCountdown();
    }

    void BeginCountdown()
    {
        if (_started)
            return;

        _started = true;
        leftGrabHint?.LockHidden();
        rightGrabHint?.LockHidden();
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        RunCountdownAsync(_runCts.Token).Forget();
    }

    async UniTaskVoid RunCountdownAsync(CancellationToken ct)
    {
        if (label == null || canvasGroup == null)
        {
            Debug.LogWarning("[RaceCountdown] Missing label or CanvasGroup.", this);
            return;
        }

        canvasGroup.alpha = 0f;
        label.transform.localScale = Vector3.one;

        for (int i = 0; i < Steps.Length; i++)
        {
            await PlayStepAsync(Steps[i], isGo: i == Steps.Length - 1, ct);
        }

        onCountdownComplete?.Invoke();
    }

    void ApplyGoImpulse()
    {
        // Fresh sample before release — held hands zero Rigidbody velocity on Quest.
        UpdateControllerVelocitySamples();
        TryGetControllerWorldVelocity(isLeft: true, out Vector3 leftVel, out bool hasLeft);
        TryGetControllerWorldVelocity(isLeft: false, out Vector3 rightVel, out bool hasRight);

        if (!TryGetFollowObject(out EntityManager em, out Entity entity))
        {
            ForceReleaseHold(leftHandHold);
            ForceReleaseHold(rightHandHold);
            Debug.LogWarning("[RaceCountdown] Player follow object not found; skipped GO! velocity.", this);
            return;
        }

        var transform = em.GetComponentData<LocalTransform>(entity);
        float3 forward = math.mul(transform.Rotation, new float3(0f, 0f, 1f));
        forward.y = 0f;
        float forwardLenSq = math.lengthsq(forward);
        if (forwardLenSq < 1e-8f)
        {
            ForceReleaseHold(leftHandHold);
            ForceReleaseHold(rightHandHold);
            Debug.LogWarning("[RaceCountdown] Player forward is degenerate; skipped GO! velocity.", this);
            return;
        }

        forward = math.normalize(forward);

        float avgLocalZ = 0f;
        int handCount = 0;
        if (hasLeft)
        {
            avgLocalZ += math.dot((float3)leftVel, forward);
            handCount++;
        }
        if (hasRight)
        {
            avgLocalZ += math.dot((float3)rightVel, forward);
            handCount++;
        }
        if (handCount > 0)
            avgLocalZ /= handCount;

        // Rearward motion (negative local Z) boosts; forward motion adds nothing.
        float handPullExtra = goHandPullSpeedScale * math.max(0f, -avgLocalZ);
        float totalSpeed = goForwardSpeed + handPullExtra;
        
        Debug.Log($"Hand pull extra: {handPullExtra}, Total speed: {totalSpeed}");
        Debug.Log($"Left hand velocity: {leftVel}, Right hand velocity: {rightVel}");

        ForceReleaseHold(leftHandHold);
        ForceReleaseHold(rightHandHold);

        if (totalSpeed <= 0f)
            return;

        var motion = em.GetComponentData<PlayerFollowObjectMotionState>(entity);
        motion.terrainRelativeVelocity += forward * totalSpeed;
        em.SetComponentData(entity, motion);
    }

    static void ForceReleaseHold(Grabbable hold)
    {
        if (hold != null && hold.IsHeld())
            hold.ForceHandsRelease();
    }

    void UpdateControllerVelocitySamples()
    {
        float now = Time.time;
        float dt = now - _controllerSampleTime;
        if (_controllerSampleTime > 0f && dt <= 1e-6f)
            return;

        TryResolveHand(leftHandHold, isLeft: true, out Hand leftHand);
        TryResolveHand(rightHandHold, isLeft: false, out Hand rightHand);

        if (leftHand != null && TryGetFollowPosition(leftHand, out Vector3 leftPos))
        {
            if (_hasLeftFollowSample && dt > 1e-6f)
                _leftControllerVelocity = (leftPos - _leftFollowSamplePos) / dt;
            _leftFollowSamplePos = leftPos;
            _hasLeftFollowSample = true;
        }

        if (rightHand != null && TryGetFollowPosition(rightHand, out Vector3 rightPos))
        {
            if (_hasRightFollowSample && dt > 1e-6f)
                _rightControllerVelocity = (rightPos - _rightFollowSamplePos) / dt;
            _rightFollowSamplePos = rightPos;
            _hasRightFollowSample = true;
        }

        _controllerSampleTime = now;
    }

    void TryGetControllerWorldVelocity(bool isLeft, out Vector3 velocity, out bool hasVelocity)
    {
        velocity = Vector3.zero;
        hasVelocity = false;

        // Prefer follow-pose deltas sampled during the countdown. Hand Rigidbody
        // velocity is zero while holding, and XR deviceVelocity is not always reliable.
        if (isLeft && _hasLeftFollowSample)
        {
            velocity = _leftControllerVelocity;
            hasVelocity = true;
            return;
        }

        if (!isLeft && _hasRightFollowSample)
        {
            velocity = _rightControllerVelocity;
            hasVelocity = true;
            return;
        }

        var node = isLeft ? XRNode.LeftHand : XRNode.RightHand;
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid &&
            device.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 deviceVel))
        {
            velocity = deviceVel;
            hasVelocity = true;
        }
    }

    static void TryResolveHand(Grabbable hold, bool isLeft, out Hand hand)
    {
        hand = null;
        if (hold != null)
        {
            var heldBy = hold.GetHeldBy();
            if (heldBy != null && heldBy.Count > 0)
                hand = heldBy[0];
        }

        if (hand != null)
            return;

        var player = AutoHandPlayer.Instance;
        if (player != null)
            hand = isLeft ? player.handLeft : player.handRight;
    }

    static bool TryGetFollowPosition(Hand hand, out Vector3 position)
    {
        position = Vector3.zero;
        if (hand == null)
            return false;

        if (hand.follow != null)
        {
            position = hand.follow.position;
            return true;
        }

        position = hand.transform.position;
        return true;
    }

    bool TryGetFollowObject(out EntityManager em, out Entity entity)
    {
        em = default;
        entity = Entity.Null;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        em = world.EntityManager;
        EnsureFollowObjectQuery(em);
        if (!_hasFollowObjectQuery || _followObjectQuery.IsEmptyIgnoreFilter)
            return false;

        entity = _followObjectQuery.GetSingletonEntity();
        return true;
    }

    void EnsureFollowObjectQuery(EntityManager em)
    {
        if (_hasFollowObjectQuery && _followObjectQuery != default)
            return;

        _followObjectQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerFollowObjectTag>(),
            ComponentType.ReadWrite<PlayerFollowObjectMotionState>(),
            ComponentType.ReadOnly<LocalTransform>());
        _hasFollowObjectQuery = true;
    }

    void DisposeFollowObjectQuery()
    {
        if (_hasFollowObjectQuery && _followObjectQuery != default)
        {
            _followObjectQuery.Dispose();
            _followObjectQuery = default;
        }

        _hasFollowObjectQuery = false;
    }

    async UniTask PlayStepAsync(string text, bool isGo, CancellationToken ct)
    {
        label.text = text;
        label.color = isGo ? goColor : digitColor;

        canvasGroup.DOKill();
        label.transform.DOKill();

        canvasGroup.alpha = 0f;
        label.transform.localScale = Vector3.one;

        if (isGo)
            ApplyGoImpulse();

        float stepStart = Time.time;

        await canvasGroup.DOFade(1f, fadeInSeconds)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .WithCancellation(ct);

        if (holdBeforeFadeOutSeconds > 0f)
            await UniTask.Delay(System.TimeSpan.FromSeconds(holdBeforeFadeOutSeconds), cancellationToken: ct);

        await UniTask.WhenAll(
            canvasGroup.DOFade(0f, fadeOutSeconds)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .WithCancellation(ct),
            label.transform.DOScale(Vector3.zero, fadeOutSeconds)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .WithCancellation(ct)
        );

        float elapsed = Time.time - stepStart;
        float remaining = stepDurationSeconds - elapsed;
        if (remaining > 0f)
            await UniTask.Delay(System.TimeSpan.FromSeconds(remaining), cancellationToken: ct);
    }
}
