using UnityEngine;

public class CameraFacing : MonoBehaviour
{
    private Transform mainCam;
    
    void LateUpdate()
    {
        if (mainCam == null || mainCam.gameObject.activeInHierarchy == false)
        {
            mainCam = Camera.main.transform;
            if (mainCam == null) 
            {
                mainCam = GameObject.FindWithTag("MainCamera").transform;
                if (mainCam == null) 
                {
                    return;
                }
            }
        }
        
        // Make the object look at the camera's position.
        transform.LookAt(mainCam.position);
    }    
}
