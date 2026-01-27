using System;
using UnityEngine;

namespace LiquidForce
{

    public class UICamera : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Camera uiCamera;

        

        
        public void OnUIVisible(bool visible)
        {
            int uiLayerMask = LayerMask.GetMask("UI", "Hand");
            
            if (uiCamera.gameObject.activeInHierarchy)
            {
                visible = false;
            }
            else
            {
                visible = true;
            }
            
            if (visible)
            {
                mainCamera.cullingMask &= ~uiLayerMask;
                uiCamera.cullingMask = uiLayerMask;
                uiCamera.gameObject.SetActive(true);
            }
            else
            {
                mainCamera.cullingMask |= uiLayerMask;
                uiCamera.gameObject.SetActive(false);
            }
        }
        
        
        
        
    }

}
