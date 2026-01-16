using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;


namespace LiquidForce {

    /// <summary>
    /// Provides camera fading functionality.
    /// </summary>
    public class CameraFader : MonoBehaviour {
        
        /// <summary>The Singleton instance of this component.</summary>
        static public CameraFader Instance { get; private set; }

        /// <summary>The default fade duration in seconds.</summary>
        protected const float DefaultFadeDurationSeconds = 0.25f;
        
        /// <summary>The fade material.</summary>
        protected static Material fadeMaterial = null;

        /// <summary>Root GameObject for the camera fader.</summary>
        protected GameObject cameraFaderRoot;

        /// <summary>Token that allows for the fade animation to be canceled.</summary>
        protected CancellationTokenSource animCancel = null;



        /// <summary>
        /// Creates objects, meshes, materials for the camera fader.
        /// </summary>
        private void Awake() {
            // If this is not the first instance, destroy this component.
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            // Store a reference to this instance.
            Instance = this;
        }

        

        private void Start()
        {
            CreateGameObjects();
            
            // Set the object to follow the head.
            DeviceTracking.Instance.AddHeadFollower(cameraFaderRoot.transform);
        }


        
        private void CreateGameObjects()
        {
            // Z offset of the fader from the camera.
            float zOffset = 0.11f;
            
            // Create objects.
            cameraFaderRoot = new GameObject("CameraFader");
            GameObject cameraFaderOffset = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            InvertMeshNormals(cameraFaderOffset.GetComponent<MeshFilter>().mesh);
            Destroy(cameraFaderOffset.GetComponent<Collider>());
            cameraFaderOffset.transform.parent = cameraFaderRoot.transform;
            cameraFaderOffset.transform.localPosition = new Vector3(0, 0, zOffset);
            cameraFaderOffset.transform.rotation = Quaternion.Euler(-90f, 0, 0);
            cameraFaderOffset.transform.localScale *= 5;
            cameraFaderOffset.layer = LayerMask.NameToLayer("UI");
            fadeMaterial = new Material(Shader.Find("LiquidForce/CameraFader"));
            cameraFaderOffset.GetComponent<MeshRenderer>().material = fadeMaterial;
        }
        
        
        public void InvertMeshNormals(Mesh mesh)
        {
            // 1. Invert the normals by multiplying each by -1
            Vector3[] normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = -normals[i];
            }
            mesh.normals = normals;

            // 2. Reverse the order of the triangles to make the inside visible
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Swap the first and second vertex of each triangle
                int temp = triangles[i + 0];
                triangles[i + 0] = triangles[i + 1];
                triangles[i + 1] = temp;
            }
            mesh.triangles = triangles;
        }        

        
        /// <summary>
        /// Called to set the camera fader completely invisible.
        /// </summary>
        public void SetCameraFadedIn() {
            if (animCancel != null) {
                animCancel.Cancel();
                animCancel.Dispose();
                animCancel = null;
            }
            cameraFaderRoot.SetActive(false);
            Color color = fadeMaterial.color;
            color.a = 0.0f;
            fadeMaterial.color = color;
        }



        /// <summary>
        /// Called to set the camera completely faded out.
        /// </summary>
        public void SetCameraFadedOut() {
            if (animCancel != null) {
                animCancel.Cancel();
                animCancel.Dispose();
                animCancel = null;
            }
            cameraFaderRoot.SetActive(true);
            Color color = fadeMaterial.color;
            color.a = 1.0f;
            fadeMaterial.color = color;
        }



        /// <summary>
        /// Called to fade the camera out.
        /// </summary>
        /// <param name="durationSeconds"></param>
        /// <returns></returns>
        public async UniTask FadeCameraOut(float durationSeconds = DefaultFadeDurationSeconds) {
            await DoFade(durationSeconds, true);
        }



        /// <summary>
        /// Called to fade the camera in.
        /// </summary>
        /// <param name="durationSeconds"></param>
        /// <returns></returns>
        public async UniTask FadeCameraIn(float durationSeconds = DefaultFadeDurationSeconds) {
            await DoFade(durationSeconds, false);
        }
        

        /// <summary>
        /// Called by public fade methods to actually perform the fade.
        /// </summary>
        /// <param name="durationSeconds"></param>
        /// <param name="isFadeOut"></param>
        /// <returns></returns>
        private async UniTask DoFade(float durationSeconds, bool isFadeOut) {
            if (animCancel != null) {
                animCancel.Cancel();
                animCancel.Dispose();
            }
            animCancel = new();
            Color color = fadeMaterial.color;
            color.a = isFadeOut ? 1.0f : 0.0f;
            cameraFaderRoot.SetActive(true);
            await fadeMaterial.DOColor(color, durationSeconds).WithCancellation(animCancel.Token);
            cameraFaderRoot.SetActive(isFadeOut);
        }
        
        
        /// <summary>
        /// Creates the material to use for the camera fader.
        /// </summary>
        /// <remarks>
        /// This is currently not used. The problem is, no standard URP shader defines a "Z Test" keyword.
        /// </remarks>
        /// <returns>The material to use with the camera fader.</returns>
        private Material CreateMaterial_NOT_USED()
        {
            fadeMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fadeMaterial.SetFloat("_Surface", 1.0f);
            fadeMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            fadeMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            fadeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            fadeMaterial.SetOverrideTag("RenderType", "Transparent");
            fadeMaterial.SetFloat("_ZWrite", 0.0f);
            fadeMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            fadeMaterial.renderQueue = (int)RenderQueue.Transparent;
            fadeMaterial.renderQueue += fadeMaterial.HasProperty("_QueueOffset") ? (int)fadeMaterial.GetFloat("_QueueOffset") : 0;
            fadeMaterial.SetShaderPassEnabled("ShadowCaster", false);
            fadeMaterial.color = new Color(0, 0, 0, 1.0f);
            return fadeMaterial;
        }

    }
}

