using System;
using UnityEngine;

public class PhysicsTest : MonoBehaviour
{
    [SerializeField] private GameObject target;
    [SerializeField] private Rigidbody testObject;



    private bool hasSetInitialVelocity = false;

    private void FixedUpdate()
    {
        if (!hasSetInitialVelocity)
        {
            testObject.linearVelocity = new Vector3(0, 0, 10);
            hasSetInitialVelocity = true;
        }

/*        
        Vector3 rotationAxis = Vector3.up;
        float angle = 40.0f * Time.deltaTime;
        Quaternion rotation = Quaternion.AngleAxis(angle, rotationAxis);
        testObject.linearVelocity = rotation * testObject.linearVelocity;
*/

        Vector3 targetDirection = target.transform.position - testObject.transform.position;
        targetDirection = Vector3.Normalize(targetDirection) * 40.0f;
        
        testObject.linearVelocity = Vector3.RotateTowards(testObject.linearVelocity, targetDirection, Time.deltaTime * 10f, 0);
        

        
        Vector3 pos = testObject.transform.position + testObject.linearVelocity;
        testObject.transform.LookAt(pos);




//        Quaternion targetRotation = Quaternion.LookRotation(testObject.transform.position - target.transform.position);
//        testObject.linearVelocity = Vector3.RotateTowards(testObject.linearVelocity, targetRotation.eulerAngles, Time.deltaTime * 10f, 0);

//        Quaternion targetRotation = Quaternion.LookRotation(target.transform.position - testObject.transform.position);




/*
        // Example: Rotate around the Y-axis based on user input
        float rotationSpeed = 100.0f;
        float turnInput = 0.5f;
        Quaternion deltaRotation = Quaternion.Euler(Vector3.up * turnInput * rotationSpeed * Time.deltaTime);
        Quaternion targetRotation = testObject.rotation * deltaRotation;

        testObject.MoveRotation(targetRotation);
 */

//        Quaternion targetRotation = Quaternion.LookRotation(target.transform.position - testObject.transform.position);
//        testObject.transform.rotation = Quaternion.Slerp(testObject.transform.rotation, targetRotation, 0.1f);
    }
}
