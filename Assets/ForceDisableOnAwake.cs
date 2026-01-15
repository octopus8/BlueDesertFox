using System;
using UnityEngine;

public class ForceDisableOnAwake : MonoBehaviour
{
    private void Awake()
    {
        gameObject.SetActive(false);
    }
}
