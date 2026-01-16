using System;
using System.Collections;
using System.Collections.Generic;
using LiquidForce;
using UnityEngine;

public class BLeeDev : MonoBehaviour
{
    static public BLeeDev instance;

    [SerializeField] public GameObject TestObject;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
//        TestObject.SetActive(false);
//        StartCoroutine(DelayedEnable());
        
        
//        CameraFader.Instance.SetCameraFadedOut();
//        _ = CameraFader.Instance.FadeCameraIn(5);
    }

/*
    IEnumerator DelayedFadeCameraIn(float time)
    {
        yield return new WaitForSeconds(time);
        _ = CameraFader.Instance.FadeCameraIn(5);
    }
*/
    public void OnTestButton()
    {
        TestObject.SetActive(false);
    }

    IEnumerator DelayedEnable()
    {
        yield return new WaitForSeconds(1);
        TestObject.SetActive(true);
    }
    
}
