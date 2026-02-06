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
//            testObject.linearVelocity = new Vector3(0, 0, 10);
            hasSetInitialVelocity = true;
        }

        Quaternion targetRotation = Quaternion.LookRotation(target.transform.position - testObject.transform.position);
        testObject.transform.rotation = Quaternion.Slerp(testObject.transform.rotation, targetRotation, 0.1f);
    }
}
