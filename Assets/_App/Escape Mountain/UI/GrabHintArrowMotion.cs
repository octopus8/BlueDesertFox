using DG.Tweening;
using UnityEngine;

/// <summary>
/// Smoothly ping-pongs a grab-hint arrow between its start local position and an offset.
/// </summary>
public class GrabHintArrowMotion : MonoBehaviour
{
    [SerializeField] private Vector3 localOffset = new Vector3(-5f, -5f, 0f);
    [SerializeField] private float durationSeconds = 0.8f;
    [SerializeField] private Ease ease = Ease.InOutSine;

    Vector3 _startLocalPosition;
    bool _hasStart;

    void OnEnable()
    {
        if (!_hasStart)
        {
            _startLocalPosition = transform.localPosition;
            _hasStart = true;
        }
        else
        {
            transform.localPosition = _startLocalPosition;
        }

        transform.DOKill();
        transform.DOLocalMove(_startLocalPosition + localOffset, durationSeconds)
            .SetEase(ease)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    void OnDisable()
    {
        transform.DOKill();
        if (_hasStart)
            transform.localPosition = _startLocalPosition;
    }

    void OnDestroy()
    {
        transform.DOKill();
    }
}
