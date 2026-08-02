using DG.Tweening;
using UnityEngine;

/// <summary>
/// Smoothly ping-pongs a grab-hint arrow between its start local position and an offset,
/// scaling from a rest factor up to full natural scale at the far end.
/// </summary>
public class GrabHintArrowMotion : MonoBehaviour
{
    [SerializeField] private Vector3 localOffset = new Vector3(-5f, -5f, 0f);
    [SerializeField] private float durationSeconds = 0.8f;
    [SerializeField] private Ease ease = Ease.InOutSine;
    [SerializeField] private float restScaleFactor = 0.75f;

    Vector3 _startLocalPosition;
    Vector3 _naturalLocalScale;
    bool _hasStart;

    void OnEnable()
    {
        if (!_hasStart)
        {
            _startLocalPosition = transform.localPosition;
            _naturalLocalScale = transform.localScale;
            _hasStart = true;
        }

        transform.localPosition = _startLocalPosition;
        transform.localScale = _naturalLocalScale * restScaleFactor;

        transform.DOKill();
        DOTween.Sequence()
            .SetTarget(transform)
            .SetUpdate(true)
            .Append(transform.DOLocalMove(_startLocalPosition + localOffset, durationSeconds).SetEase(ease))
            .Join(transform.DOScale(_naturalLocalScale, durationSeconds).SetEase(ease))
            .SetLoops(-1, LoopType.Yoyo);
    }

    void OnDisable()
    {
        transform.DOKill();
        if (_hasStart)
        {
            transform.localPosition = _startLocalPosition;
            transform.localScale = _naturalLocalScale * restScaleFactor;
        }
    }

    void OnDestroy()
    {
        transform.DOKill();
    }
}
