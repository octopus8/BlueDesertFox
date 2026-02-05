using System;
using UnityEngine;

public class PhysicsTest : MonoBehaviour
{
    [SerializeField] private Rigidbody testObject;


    private void FixedUpdate()
    {
        testObject.linearVelocity = new Vector3(0, 0, -10);
    }
}
