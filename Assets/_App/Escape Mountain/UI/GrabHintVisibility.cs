using Autohand;
using UnityEngine;

/// <summary>
/// Shows a grab-hint message when its handle is free, hides it while held,
/// and can lock permanently hidden once the race countdown starts.
/// Lives on the hand-hold (not the message) so release events still fire after hide.
/// </summary>
public class GrabHintVisibility : MonoBehaviour
{
    [SerializeField] private Grabbable handHold;
    [SerializeField] private GameObject grabMessage;

    bool _locked;

    void OnEnable()
    {
        if (handHold != null)
        {
            handHold.OnGrabEvent += OnGrabbed;
            handHold.OnReleaseEvent += OnReleased;
        }

        if (!_locked)
            SyncVisibility();
    }

    void OnDisable()
    {
        if (handHold != null)
        {
            handHold.OnGrabEvent -= OnGrabbed;
            handHold.OnReleaseEvent -= OnReleased;
        }
    }

    public void LockHidden()
    {
        _locked = true;
        if (grabMessage != null)
            grabMessage.SetActive(false);
    }

    void OnGrabbed(Hand hand, Grabbable grab)
    {
        if (_locked || grabMessage == null)
            return;
        grabMessage.SetActive(false);
    }

    void OnReleased(Hand hand, Grabbable grab)
    {
        if (_locked || grabMessage == null)
            return;
        grabMessage.SetActive(true);
    }

    void SyncVisibility()
    {
        if (grabMessage == null || handHold == null)
            return;
        grabMessage.SetActive(!handHold.IsHeld());
    }
}
