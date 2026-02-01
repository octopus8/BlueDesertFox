using System;
using App.StartScene;
using TMPro;
using UnityEngine;

public class BreadcrumbUI : MonoBehaviour
{
    [SerializeField] protected TextMeshProUGUI  breadcrumbText;
    
    [SerializeField] protected UIManager uiManager;

    [SerializeField] protected string separator = "-";


    private void OnEnable()
    {
        UpdateText();
    }


    private void Start()
    {
        UpdateText();
    }


    private void UpdateText()
    {
        breadcrumbText.text = string.Join(separator, uiManager.GetStackNames());
    }
}
