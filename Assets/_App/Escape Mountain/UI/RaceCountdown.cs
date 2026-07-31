using System.Threading;
using Autohand;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// World-space race start countdown (3-2-1-GO!). Starts when both hand-hold Grabbables
/// are held, then plays each value with a quick fade-in and a slower top-anchored
/// shrink + fade-out.
/// </summary>
public class RaceCountdown : MonoBehaviour
{
    static readonly string[] Steps = { "3", "2", "1", "GO!" };

    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private CanvasGroup canvasGroup;

    [SerializeField] private Grabbable leftHandHold;
    [SerializeField] private Grabbable rightHandHold;

    [SerializeField] private float fadeInSeconds = 0.12f;
    [SerializeField] private float holdBeforeFadeOutSeconds = 0.15f;
    [SerializeField] private float fadeOutSeconds = 0.7f;
    [SerializeField] private float stepDurationSeconds = 1f;

    [SerializeField] private Color digitColor = new Color(0.78f, 0.92f, 1f, 1f);
    [SerializeField] private Color goColor = new Color(0.45f, 1f, 0.2f, 1f);
    [SerializeField] private float outlineWidth = 0.3f;
    [SerializeField] private Color outlineColor = new Color(0.02f, 0.05f, 0.12f, 1f);

    [SerializeField] private UnityEvent onCountdownComplete;

    CancellationTokenSource _runCts;
    bool _started;

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

    void OnDisable()
    {
        Unsubscribe(leftHandHold);
        Unsubscribe(rightHandHold);
    }

    void OnDestroy()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;

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

        _started = true;
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

    async UniTask PlayStepAsync(string text, bool isGo, CancellationToken ct)
    {
        label.text = text;
        label.color = isGo ? goColor : digitColor;

        canvasGroup.DOKill();
        label.transform.DOKill();

        canvasGroup.alpha = 0f;
        label.transform.localScale = Vector3.one;

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
