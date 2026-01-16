using System;
using UnityEngine;

[DefaultExecutionOrder(-32000)]
public class ForceDisableOnAwake : MonoBehaviour
{
    private void Awake()
    {
        gameObject.SetActive(false);
    }
}
