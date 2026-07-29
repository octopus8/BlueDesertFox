using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Lightweight MonoBehaviour that moves a Rigidbody along a Unity <see cref="SplineContainer"/>
/// using physics-based steering. Each <c>FixedUpdate</c> the object advances its position ratio
/// along the spline, steers its <see cref="Rigidbody.linearVelocity"/> toward the spline point, and
/// orients itself using the spline's up vector. The spline loops continuously.
/// <para>Used for prototyping spline movement without the ECS spline follower system.</para>
/// </summary>
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
    
    
    /// <summary>
    /// Advances <c>distanceRatio</c> along the spline proportional to <see cref="speed"/> and elapsed
    /// fixed-delta time, wraps at 1.0 for continuous looping, evaluates the spline position/tangent/up,
    /// updates the optional <c>splineLocation</c> marker, and steers the Rigidbody toward the target point.
    /// </summary>
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
