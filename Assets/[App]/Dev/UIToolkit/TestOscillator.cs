using UnityEngine;

public class TestOscillator : MonoBehaviour
{
    private float testval = 0;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        testval = (Mathf.Sin(Time.time) + 1.0f) * 0.5f;
//        Debug.Log(testval);
    }
}
