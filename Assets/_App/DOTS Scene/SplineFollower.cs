using UnityEngine;
using UnityEngine.Splines;

public class SplineFollower : MonoBehaviour
{
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private Rigidbody rb;
    
    // The speed at which the object moves along the spline
    [SerializeField] private float speed = 5f;

    [SerializeField] private GameObject splineLocation;

    // A value from 0 to 1 representing the object's position along the spline
    private float distanceRatio = 0f;    

    
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

        // Get the position and tangent (forward direction) at the current point on the spline
        Vector3 position = splineContainer.EvaluatePosition(distanceRatio);
        Vector3 tangent = splineContainer.EvaluateTangent(distanceRatio);
        Vector3 upVector = splineContainer.EvaluateUpVector(distanceRatio);
        
        splineLocation.transform.position = position;

        // Calculate rotation to align the object with the spline's direction
        // The second argument, Vector3.up, helps define the 'up' direction
        Quaternion rotation = Quaternion.LookRotation(tangent, upVector);
        
        Vector3 moveDirection = position - transform.position;
        moveDirection = Vector3.Normalize(moveDirection);
//        rb.rotation = Quaternion.LookRotation(moveDirection, Vector3.up);
//        rb.linearVelocity = moveDirection * speed;
//        rb.angularVelocity = Vector3.zero;
        
        

        // Apply the position and rotation using Rigidbody's MovePosition/MoveRotation
        // This is crucial for physics interactions
        rb.gameObject.transform.position = position;
        rb.gameObject.transform.rotation = rotation;
//        rb.MovePosition(position);
//        rb.MoveRotation(rotation);
    }    
}
