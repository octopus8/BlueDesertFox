using UnityEngine;

namespace LiquidForce
{
    /// <summary>
    /// This component sets the UI camera active when the UI is visible.
    /// </summary>
    public class UICamera : MonoBehaviour
    {
        [Tooltip("The main camera used to display the scene.")]
        [SerializeField] private Camera mainCamera;
        
        [Tooltip("The camera used to display the UI layer.")]
        [SerializeField] private Camera uiCamera;

        
        
        /// <summary>
        /// Sets the UI camera's active state and sets culling masks appropriately.
        /// </summary>
        /// <param name="visible"></param>
        public void OnUIVisible(bool visible)
        {
            int uiLayerMask = LayerMask.GetMask("UI", "Hand");
            
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
