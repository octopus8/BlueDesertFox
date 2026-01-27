using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiquidForce;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;


namespace App.StartScene
{
    
    public class UI : MonoBehaviour
    {
        /// <summary>"Fade in/out duration in seconds."</summary>
        [Tooltip("Fade in/out duration in seconds.")]
        [SerializeField] private float displaySpeed = 0.5f;

        /// <summary>UI container, used to animate the UI.</summary>
        [Tooltip("UI container, used to animate the UI.")]
        [SerializeField] private CanvasGroup uiContainer;

        /// <summary>Scene list.</summary>
        [Tooltip("Scene list.")]
        [SerializeField] private SceneListSO sceneList;

        /// <summary>Scene list button container.</summary>
        [Tooltip("Scene list button container")]
        [SerializeField] private GameObject sceneListContainer;
        
        /// <summary>Prototype scene list button.</summary>
        [Tooltip("Prototype scene list button.")]
        [SerializeField] private SceneListButton prototypeButton;

        /// <summary>Flag indicating whether or not to display the UI on start.</summary>
        [Tooltip("Flag indicating whether or not to display the UI on start.")]
        [SerializeField] private bool displayOnStart = false;

        /// <summary>Menu toggle action.</summary>
        private InputAction menuToggleAction;

        /// <summary>Test action.</summary>
        private InputAction testAction;
        
        /// <summary>Token that allows for the fade animation to be canceled.</summary>
        private CancellationTokenSource[] animCancelTokens = new CancellationTokenSource[System.Enum.GetValues(typeof(AnimCancelToken)).Length];

        private AnimState currentAnimState = AnimState.off;
        
        private bool testActionBool = false;


        /// <summary>
        /// Animation states.
        /// </summary>
        enum AnimState
        {
            off,
            turningOn,
            on,
            turningOff
        }

        
        /// <summary>Async animations</summary>
        enum AnimCancelToken
        {
            fade,
            scale
        }

        
        /// <summary>
        /// Initializes the UI.
        /// </summary>
        void Start()
        {
            // Create and initialize scene list buttons.
            foreach (SceneListSO.SceneListScene scene in sceneList.scenes)
            {
                SceneListButton sceneListButton = Instantiate(prototypeButton.gameObject, sceneListContainer.transform)
                    .GetComponent<SceneListButton>();
                sceneListButton.Init(scene.sceneName, scene.scene);
            }
            prototypeButton.gameObject.SetActive(false);
            
            // Enable actions.
            menuToggleAction = InputSystem.actions.FindAction("ToggleMenu"); 
            menuToggleAction.Enable(); // Actions must be enabled to receive input            
            testAction = InputSystem.actions.FindAction("TestAction");
            testAction.Enable();

            // Display on start if indicated.
            if (!displayOnStart)
            {
                SetHiddenImmediate();
            }
            else
            {
                currentAnimState = AnimState.on;
            }
        }


        /// <summary>
        /// Processes input
        /// </summary>
        private void Update()
        {
            // Handle test action.
            if (testAction.WasPressedThisFrame())
            {
                if (testActionBool)
                {
                    testActionBool = false;
                    _ = CameraFader.Instance.FadeCameraIn(10);
                }
                else
                {
                    testActionBool = true;
                    _ = CameraFader.Instance.FadeCameraOut(10);
                }
            }
            
            // Handle menu toggle action.
            if (menuToggleAction.WasPressedThisFrame())
            {
                ToggleVisibility();
            }            
        }
        
        
        /// <summary>
        /// Shows the UI.
        /// </summary>
        public void Show()
        {
            CancelAnimations();
            currentAnimState = AnimState.turningOn;
            uiContainer.gameObject.SetActive(true);
            uiContainer.DOFade(1, displaySpeed).WithCancellation(animCancelTokens[(int)AnimCancelToken.fade].Token);
            uiContainer.transform.DOScale(new Vector3(1, 1, 1), displaySpeed)
                .WithCancellation(animCancelTokens[(int)AnimCancelToken.scale].Token).ContinueWith(() =>
                {
                    currentAnimState =  AnimState.on;
                });
        }

        
        /// <summary>
        /// Hides the UI.
        /// </summary>
        public void Hide()
        {
            CancelAnimations();
            currentAnimState = AnimState.turningOff;
            uiContainer.DOFade(0, displaySpeed).WithCancellation(animCancelTokens[(int)AnimCancelToken.fade].Token);
            uiContainer.transform.DOScale(new Vector3(0, 0, 1), displaySpeed)
                .WithCancellation(animCancelTokens[(int)AnimCancelToken.scale].Token).ContinueWith(() =>
                {
                    uiContainer.gameObject.SetActive(false);
                    currentAnimState =  AnimState.off;
                });
        }


        /// <summary>
        /// Loads the scene associated with the button.
        /// </summary>
        /// <param name="button"></param>
        public void LoadScene(SceneListButton button)
        {
            Hide();
            _ = CameraFader.Instance.FadeCameraOut(1);
            AsyncOperationHandle<SceneInstance> loadSceneHandle = Addressables.LoadSceneAsync( button.GetAssetReference(), LoadSceneMode.Single, false);
            StartCoroutine(ActivateLoadedSceneOnLoad(loadSceneHandle));
        }


        /// <summary>
        /// Toggles visibility.
        /// </summary>
        private void ToggleVisibility()
        {
            if (currentAnimState is AnimState.on or AnimState.turningOn)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        
        /// <summary>
        /// Cancels animations.
        /// </summary>
        private void CancelAnimations()
        {
            for (var i=0; i < animCancelTokens.Length; ++ i)
            {
                var token = animCancelTokens[i];
                if (token != null)
                {
                    token.Cancel();
                    token.Dispose();
                }
                animCancelTokens[i] = new CancellationTokenSource();
            }
        }
        

        /// <summary>
        /// Activates the loaded scene upon fully loading and camera fully faded out.
        /// </summary>
        /// <param name="loadSceneHandle"></param>
        /// <returns></returns>
        private IEnumerator ActivateLoadedSceneOnLoad(AsyncOperationHandle<SceneInstance> loadSceneHandle)
        {
            // Wait for the scene to be loaded and the camera to fully fade out.
            while (!loadSceneHandle.IsDone || !CameraFader.Instance.IsCameraFadedOut())
            {
                yield return null;
            }
            
            // Activate the loaded scene.
            if (loadSceneHandle.Status == AsyncOperationStatus.Succeeded)
            {
                loadSceneHandle.Result.ActivateAsync();
            }
            else
            {
                CameraFader.Instance.SetCameraFadedIn();
                Debug.LogError("Could not load scene: " + loadSceneHandle.Status);
            }
        }


        /// <summary>
        /// Sets the UI as hidden immediately.
        /// </summary>
        /// <remarks>
        /// This is used to make sure the UI is not visible at the start if `displayOnStart` is false.
        /// </remarks>
        private void SetHiddenImmediate()
        {
            CancelAnimations();
            uiContainer.transform.localScale = new Vector3(0, 0, 1);
            uiContainer.alpha = 0;
            uiContainer.gameObject.SetActive(false);
            currentAnimState = AnimState.off;
        }
    }
}

