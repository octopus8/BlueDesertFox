using System.Collections;
using UnityEngine;

public class DottyApp : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(TestFunc());
    }
    

    IEnumerator TestFunc()
    {
        yield return new WaitForSeconds(3);
        DoTestSpawn();
    }


    private void DoTestSpawn()
    {
        
    }
}
