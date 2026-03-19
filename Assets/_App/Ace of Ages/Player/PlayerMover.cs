using UnityEngine;

public class PlayerMover : MonoBehaviour
{
    [SerializeField]
    private Transform trackingOrigin;
    
    [SerializeField]
    private Transform leftController;
    
    [SerializeField]
    private Transform rightController;

    [SerializeField] private bool isTrackingLeftController = false;

    [SerializeField] private float speed = 1.0f;
    

    private void FixedUpdate()
    {
        if (isTrackingLeftController)
        {
            UpdateTrackingOrigin(leftController);
        }
        else
        {
            UpdateTrackingOrigin(rightController);
        }
    }

    private void UpdateTrackingOrigin(Transform controller)
    {
        trackingOrigin.position += controller.forward * speed * Time.fixedDeltaTime;
    }


}
