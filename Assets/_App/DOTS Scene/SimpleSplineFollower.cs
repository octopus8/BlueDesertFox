using UnityEngine;
using UnityEngine.Splines;

public class SimpleSplineFollower : MonoBehaviour
{
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private Rigidbody rb;
    
    // The speed at which the object moves along the spline
    [SerializeField] private float speed = 5f;

    [SerializeField] private GameObject splineLocation;

    // A value from 0 to 1 representing the object's position along the spline
    private float distanceRatio = 0f;    

    private bool hasSetInitialVelocity = false;
    
    
    void FixedUpdate()
    {
        // Calculate the new distance ratio based on speed and time
        distanceRatio += (speed * Time.fixedDeltaTime) / splineContainer.CalculateLength();
        
        // Wrap around the spline if it's a closed loop, or stop at the end
        if (distanceRatio > 1f)
        {
            distanceRatio -= 1f; // For looping paths
            // For non-looping paths, you might want to stop or reverse
            // distanceRatio = 1f; enabled = false; 
        }

        // Compute spline values for the distance value.
        Vector3 position = splineContainer.EvaluatePosition(distanceRatio);
        Vector3 tangent = splineContainer.EvaluateTangent(distanceRatio);
        Vector3 upVector = splineContainer.EvaluateUpVector(distanceRatio);

        // Set the position of the test spline location object.
        if (splineLocation != null)
        {
            splineLocation.transform.position = position;
        }
        
        // Rotate the linear velocity towards the target.
        Vector3 targetDirection = position - rb.transform.position;
        rb.linearVelocity = Vector3.RotateTowards(rb.linearVelocity, targetDirection, Time.deltaTime * 10f, 0);

        // Rotate the GameObject towards the linear velocity.
        Vector3 pos = rb.transform.position + rb.linearVelocity;
        rb.transform.LookAt(pos, upVector);
    }    
}
