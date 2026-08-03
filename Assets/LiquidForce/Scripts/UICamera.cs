using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LiquidForce
{
    /// <summary>
    /// Owns a URP Overlay camera that draws UI and Hand layers with a cleared depth buffer
    /// so world-space menus are not occluded by scene / Entities Graphics content.
    /// </summary>
    public class UICamera : MonoBehaviour
    {
        const string OverlayCameraName = "UI Camera";

        [Tooltip("The main camera used to display the scene.")]
        [SerializeField] private Camera mainCamera;

        [Tooltip("The camera used to display the UI layer.")]
        [SerializeField] private Camera uiCamera;

        void Awake()
        {
            EnsureConfigured();
        }

        /// <summary>
        /// Finds an existing <see cref="UICamera"/> (including inactive) or creates one under
        /// <see cref="Camera.main"/> with a depth-clearing Overlay camera in the URP stack.
        /// </summary>
        public static UICamera EnsureExists()
        {
            var existing = FindAnyObjectByType<UICamera>(FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.EnsureConfigured();
                return existing;
            }

            var main = Camera.main;
            if (main == null)
            {
                Debug.LogWarning("[UICamera] No main camera found; cannot create overlay UI camera.");
                return null;
            }

            var host = main.gameObject.AddComponent<UICamera>();
            host.mainCamera = main;
            host.EnsureConfigured();
            return host;
        }

        /// <summary>
        /// Ensures main/overlay camera references exist and the overlay is on the Base camera stack.
        /// </summary>
        public void EnsureConfigured()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (mainCamera == null)
                return;

            if (uiCamera == null)
                CreateOverlayCamera();
            else
                EnsureInCameraStack();
        }

        /// <summary>
        /// Sets the UI camera's active state and sets culling masks appropriately.
        /// </summary>
        public void OnUIVisible(bool visible)
        {
            EnsureConfigured();
            if (mainCamera == null || uiCamera == null)
                return;

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

        void CreateOverlayCamera()
        {
            Transform existing = mainCamera.transform.Find(OverlayCameraName);
            GameObject overlayGo;
            if (existing != null)
            {
                overlayGo = existing.gameObject;
                uiCamera = overlayGo.GetComponent<Camera>();
                if (uiCamera == null)
                    uiCamera = overlayGo.AddComponent<Camera>();
            }
            else
            {
                overlayGo = new GameObject(OverlayCameraName);
                overlayGo.transform.SetParent(mainCamera.transform, false);
                overlayGo.transform.localPosition = Vector3.zero;
                overlayGo.transform.localRotation = Quaternion.identity;
                overlayGo.transform.localScale = Vector3.one;
                uiCamera = overlayGo.AddComponent<Camera>();
            }

            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.cullingMask = LayerMask.GetMask("UI", "Hand");
            uiCamera.nearClipPlane = mainCamera.nearClipPlane;
            uiCamera.farClipPlane = mainCamera.farClipPlane;
            uiCamera.depth = mainCamera.depth + 1;
            uiCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            uiCamera.allowHDR = false;
            uiCamera.allowMSAA = mainCamera.allowMSAA;

            var overlayData = uiCamera.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            // clearDepth is read-only and defaults to true on UniversalAdditionalCameraData.
            overlayData.renderShadows = false;
            overlayData.allowXRRendering = true;

            overlayGo.SetActive(false);
            EnsureInCameraStack();
        }

        void EnsureInCameraStack()
        {
            if (mainCamera == null || uiCamera == null)
                return;

            var baseData = mainCamera.GetUniversalAdditionalCameraData();
            if (baseData.renderType != CameraRenderType.Base)
                baseData.renderType = CameraRenderType.Base;

            if (!baseData.cameraStack.Contains(uiCamera))
                baseData.cameraStack.Add(uiCamera);

            var overlayData = uiCamera.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
        }
    }
}
