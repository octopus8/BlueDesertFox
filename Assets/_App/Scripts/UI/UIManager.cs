using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiquidForce;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 
/// </summary>
/// <remarks>
/// - This component is part of a prefab that has an ObjectFollower to follow the head. This component could add
/// an ObjectFollower component and set it up. This would remove the need to set the source of the ObjectFollower
/// when the prefab is added to a scene.
/// </remarks>
[RequireComponent(typeof(ObjectFollower))]
public class UIManager : MonoBehaviour
{
    /// <summary>Set to disable closing UI.</summary>
    [Tooltip("Set to disable closing UI.")]
    [SerializeField] private bool disableClose = false;

    /// <summary>"Fade in/out duration in seconds."</summary>
    [Tooltip("Fade in/out duration in seconds.")]
    [SerializeField] private float displaySpeed = 0.5f;

    /// <summary>UI container, used to animate the UI.</summary>
    [Tooltip("UI container, used to animate the UI.")]
    [SerializeField] private CanvasGroup uiContainer;

    [SerializeField] private UIState startState;



    /// <summary>UI Camera</summary>
    private UICamera uiCamera;

    /// <summary>Menu toggle action.</summary>
    private InputAction menuToggleAction;
    
    /// <summary>Token that allows for the fade animation to be canceled.</summary>
    private CancellationTokenSource[] animCancelTokens = new CancellationTokenSource[System.Enum.GetValues(typeof(AnimCancelToken)).Length];

    private AnimState currentAnimState = AnimState.off;
    
    private bool testActionBool = false;
    
    private Stack<IUIState> stateStack = new Stack<IUIState>();

    private ObjectFollower objectFollower;

    /// <summary>
    /// Raised when the UI becomes visible or hidden.
    /// First argument is visibility; second is whether gameplay should resume on hide
    /// (ignored when becoming visible).
    /// </summary>
    public event System.Action<bool, bool> VisibilityChanged;

    /// <summary>True while the UI is shown or animating in.</summary>
    public bool IsVisible => currentAnimState is AnimState.on or AnimState.turningOn;

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


    private void Awake()
    {
        // Prefer an authored UICamera (e.g. Auto Hand Player); otherwise create Overlay under Camera.main.
        uiCamera = UICamera.EnsureExists();
        objectFollower = GetComponent<ObjectFollower>();
        
        SetHiddenImmediate();
    }

    
    /// <summary>
    /// Initializes input actions when the component is enabled.
    /// </summary>
    /// <remarks>
    /// Called in OnEnable instead of Start to handle scene transitions where InputSystem.actions
    /// might be reset. This ensures the action is re-initialized if the UIManager persists across scenes.
    /// </remarks>
    private void OnEnable()
    {
        // Initialize or re-initialize the menu toggle action.
        InitializeInputAction();
    }

    
    /// <summary>
    /// Cleans up input actions when the component is disabled.
    /// </summary>
    private void OnDisable()
    {
        // Disable the action to prevent it from consuming input when UI is not active.
        if (menuToggleAction != null)
        {
            menuToggleAction.Disable();
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
#if UNITY_EDITOR
        return;
#endif
        if (pauseStatus)
            TryShowImmediateOnSystemMenu();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
#if UNITY_EDITOR
        return;
#endif
        if (!hasFocus)
            TryShowImmediateOnSystemMenu();
    }

    /// <summary>
    /// Opens the menu immediately when the Quest system menu (right Meta) interrupts the app.
    /// </summary>
    private void TryShowImmediateOnSystemMenu()
    {
        if (!IsVisible)
            ShowImmediate();
    }


    /// <summary>
    /// Initializes the UI.
    /// </summary>
    void Start()
    {
        // Push the start state.
        if (startState != null)
        {
            PushState(startState);
        }
        else
        {
            Debug.LogWarning("No start state set for UI.");
        }
    }
    
    
    /// <summary>
    /// Initializes or re-initializes the menu toggle input action.
    /// </summary>
    private void InitializeInputAction()
    {
        // Check if InputSystem.actions is available.
        if (InputSystem.actions == null)
        {
            Debug.LogWarning("UIManager: InputSystem.actions is null. Menu toggle action cannot be initialized. Ensure an InputActionAsset is set in Project Settings > Input System Package > Input Actions.");
            menuToggleAction = null;
            return;
        }
        
        // Find and enable the menu toggle action.
        menuToggleAction = InputSystem.actions.FindAction("ToggleMenu");
        
        if (menuToggleAction == null)
        {
            Debug.LogWarning("UIManager: ToggleMenu action not found in InputSystem.actions. Menu button will not work.");
            return;
        }
        
        menuToggleAction.Enable();
    }
    
    // Push a new state: Pause current, add new to top
    public void PushState(IUIState newState) {
        if (stateStack.Count > 0) {
            stateStack.Peek().OnPushed();
        }
        stateStack.Push(newState);
        newState.OnEnter();
    }

    public void PushModal(IUIState newState) {
        if (stateStack.Count > 0) {
            stateStack.Peek().OnModalPushed();
        }
        stateStack.Push(newState);
        newState.OnEnter();
    }
    
    public void PopModalPush(IUIState newState) {
        if (stateStack.Count > 0) {
            stateStack.Pop().OnExit();
        }
        if (stateStack.Count > 0) {
            stateStack.Peek().OnPushed();
            PushState(newState);
        }
    }

    // Pop state: Remove current, resume previous
    public void PopState() {
        if (stateStack.Count > 0) {
            stateStack.Pop().OnExit();
        }
        if (stateStack.Count > 0) {
            stateStack.Peek().OnPopped();
        }
    }


    public string[] GetStackNames()
    {
        List<string> names = new List<string>();
        foreach (UIState state in stateStack)
        {
            names.Add(state.stateName);
        }

        names.Reverse();
        return names.ToArray();
    }
    


    /// <summary>
    /// Processes input
    /// </summary>
    private void Update()
    {
        // Check if the menu toggle action is valid. If not, try to re-initialize it.
        if (menuToggleAction == null || !menuToggleAction.enabled)
        {
            InitializeInputAction();
            
            // If still null after initialization attempt, skip input processing this frame.
            if (menuToggleAction == null)
            {
                return;
            }
        }
        
        // Handle menu toggle action.
        if (menuToggleAction.WasPressedThisFrame())
        {
            if ((currentAnimState is AnimState.on or AnimState.turningOn && !disableClose) || (currentAnimState is AnimState.off or AnimState.turningOff))
            {
                ToggleVisibility();
            }
        }            
    }

    
    /// <summary>
    /// Shows the UI.
    /// </summary>
    public void Show()
    {
        CancelAnimations();
        currentAnimState = AnimState.turningOn;
        VisibilityChanged?.Invoke(true, false);
        EnsureUICamera()?.OnUIVisible(true);
        uiContainer.gameObject.SetActive(true);
        uiContainer.DOFade(1, displaySpeed).WithCancellation(animCancelTokens[(int)AnimCancelToken.fade].Token);
        uiContainer.transform.DOScale(new Vector3(1, 1, 1), displaySpeed)
            .WithCancellation(animCancelTokens[(int)AnimCancelToken.scale].Token).ContinueWith(() =>
            {
                currentAnimState =  AnimState.on;
            });
        objectFollower.UpdateImmediate();
    }

    /// <summary>
    /// Shows the UI immediately with no fade/scale animation.
    /// </summary>
    public void ShowImmediate()
    {
        CancelAnimations();
        currentAnimState = AnimState.on;
        VisibilityChanged?.Invoke(true, false);
        EnsureUICamera()?.OnUIVisible(true);
        uiContainer.gameObject.SetActive(true);
        uiContainer.alpha = 1;
        uiContainer.transform.localScale = Vector3.one;
        objectFollower.UpdateImmediate();
    }

    
    /// <summary>
    /// Hides the UI.
    /// </summary>
    /// <param name="resumeGameplay">
    /// When true (default), subscribers may resume gameplay (e.g. menu dismissed via toggle).
    /// When false, gameplay stays paused (e.g. scene select starting a load).
    /// </param>
    public void Hide(bool resumeGameplay = true)
    {
        CancelAnimations();
        VisibilityChanged?.Invoke(false, resumeGameplay);
        EnsureUICamera()?.OnUIVisible(false);
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
    /// Toggles visibility.
    /// </summary>
    public void ToggleVisibility()
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
        for (var i=0; i < animCancelTokens?.Length; ++ i)
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
    /// Sets the UI as hidden immediately.
    /// </summary>
    /// <remarks>
    /// This is used to make sure the UI is not visible at the start if `displayOnStart` is false.
    /// </remarks>
    private void SetHiddenImmediate()
    {
        CancelAnimations();
        uiCamera?.OnUIVisible(false);
        uiContainer.transform.localScale = new Vector3(0, 0, 1);
        uiContainer.alpha = 0;
        uiContainer.gameObject.SetActive(false);
        currentAnimState = AnimState.off;
    }

    /// <summary>
    /// Resolves or creates the Overlay UI camera. Retries if Awake ran before Camera.main existed.
    /// </summary>
    private UICamera EnsureUICamera()
    {
        if (uiCamera == null)
            uiCamera = UICamera.EnsureExists();
        return uiCamera;
    }
}

