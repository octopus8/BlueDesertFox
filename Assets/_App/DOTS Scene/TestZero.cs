using UnityEngine;



[RequireComponent(typeof(Rigidbody))]
public class TestZero : MonoBehaviour
{
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.linearVelocity = new Vector3(0, 0, -20);
    }
}
