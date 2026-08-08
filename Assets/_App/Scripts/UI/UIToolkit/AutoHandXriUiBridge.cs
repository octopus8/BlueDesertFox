using Autohand;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Keeps AutoHand hand meshes, disables AutoHand uGUI lasers, and attaches XRI UI rays
/// under each RobotHand (same parent as the old UIPointer) for UI Toolkit world-space panels.
/// Rays use the Hand layer so <see cref="LiquidForce.UICamera"/> draws them over the environment.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class AutoHandXriUiBridge : MonoBehaviour
{
    const string LeftInteractionMap = "XRI Left Interaction";
    const string RightInteractionMap = "XRI Right Interaction";
    const string HandLayerName = "Hand";

    // Matches AutoHand UIPointer.prefab local pose under the RobotHand.
    static readonly Vector3 UiPointerLocalPosition = new Vector3(0f, 0.04f, 0f);
    static readonly Quaternion UiPointerLocalRotation = Quaternion.Euler(-9f, 0f, 0f);

    [SerializeField] GameObject rayInteractorPrefab;
    [SerializeField] InputActionAsset xriInputActions;
    [SerializeField] GameObject uiHitReticlePrefab;

    void Awake()
    {
        if (xriInputActions != null)
            xriInputActions.Enable();

        HandCanvasPointer[] pointers = FindObjectsByType<HandCanvasPointer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        DisableHandCanvasPointers(pointers);
        EnsureInteractionManager();
        SetupRaysFromPointers(pointers);
    }

    static void DisableHandCanvasPointers(HandCanvasPointer[] pointers)
    {
        for (int i = 0; i < pointers.Length; i++)
        {
            if (pointers[i] != null)
                pointers[i].gameObject.SetActive(false);
        }
    }

    static void EnsureInteractionManager()
    {
        if (FindFirstObjectByType<XRInteractionManager>() != null)
            return;

        var go = new GameObject("XR Interaction Manager");
        go.AddComponent<XRInteractionManager>();
    }

    void SetupRaysFromPointers(HandCanvasPointer[] pointers)
    {
        if (rayInteractorPrefab == null || xriInputActions == null)
        {
            Debug.LogError("AutoHandXriUiBridge: rayInteractorPrefab or xriInputActions is not assigned.");
            return;
        }

        if (pointers == null || pointers.Length == 0)
        {
            Debug.LogError("AutoHandXriUiBridge: no HandCanvasPointer instances found to attach rays.");
            return;
        }

        int handLayer = LayerMask.NameToLayer(HandLayerName);
        if (handLayer < 0)
            Debug.LogWarning($"AutoHandXriUiBridge: layer '{HandLayerName}' not found; rays may be occluded by the environment.");

        for (int i = 0; i < pointers.Length; i++)
        {
            HandCanvasPointer pointer = pointers[i];
            if (pointer == null)
                continue;

            Transform handRoot = pointer.transform.parent;
            if (handRoot == null)
            {
                Debug.LogError("AutoHandXriUiBridge: HandCanvasPointer has no parent (expected RobotHand).");
                continue;
            }

            bool isLeft = ResolveIsLeftHand(handRoot);
            InteractorHandedness handedness = isLeft ? InteractorHandedness.Left : InteractorHandedness.Right;
            string actionMapName = isLeft ? LeftInteractionMap : RightInteractionMap;

            GameObject rayGo = Instantiate(rayInteractorPrefab, handRoot);
            rayGo.name = $"XRI UI Ray ({handedness})";
            rayGo.transform.localPosition = UiPointerLocalPosition;
            rayGo.transform.localRotation = UiPointerLocalRotation;
            rayGo.transform.localScale = Vector3.one;

            if (handLayer >= 0)
                SetLayerRecursive(rayGo.transform, handLayer);

            XRRayInteractor ray = rayGo.GetComponent<XRRayInteractor>();
            if (ray == null)
            {
                Debug.LogError("AutoHandXriUiBridge: Ray Interactor prefab missing XRRayInteractor.");
                continue;
            }

            ray.handedness = handedness;
            ray.enableUIInteraction = true;
            WireButton(ray.selectInput, actionMapName, "Select", "Select Value");
            WireButton(ray.uiPressInput, actionMapName, "UI Press", "UI Press Value");

            AssignUiHitReticle(rayGo, handLayer);
        }
    }

    void AssignUiHitReticle(GameObject rayGo, int handLayer)
    {
        if (uiHitReticlePrefab == null)
        {
            Debug.LogWarning("AutoHandXriUiBridge: uiHitReticlePrefab is not assigned; UI hit ring will not show.");
            return;
        }

        // Do not use XRInteractorLineVisual.reticle — it only shows for valid XR interactable
        // targets, and UITK world-space panels fail that check (trigger collider, not hovered).
        UiRayHitRingVisual ringVisual = rayGo.GetComponent<UiRayHitRingVisual>();
        if (ringVisual == null)
            ringVisual = rayGo.AddComponent<UiRayHitRingVisual>();

        ringVisual.Initialize(uiHitReticlePrefab, scale: 0.012f, layer: handLayer);
    }

    static bool ResolveIsLeftHand(Transform handRoot)
    {
        Hand hand = handRoot.GetComponentInParent<Hand>();
        if (hand != null)
            return hand.left;

        string name = handRoot.name;
        if (name.IndexOf("(L)", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (name.IndexOf("(R)", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        // Default: treat first unresolved as left is unsafe; prefer right only if marked.
        Debug.LogWarning($"AutoHandXriUiBridge: could not resolve handedness for '{name}', defaulting to Left.");
        return true;
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }

    void WireButton(XRInputButtonReader reader, string mapName, string performedName, string valueName)
    {
        InputAction performed = xriInputActions.FindAction($"{mapName}/{performedName}", throwIfNotFound: false);
        InputAction value = xriInputActions.FindAction($"{mapName}/{valueName}", throwIfNotFound: false);
        if (performed == null || value == null)
        {
            Debug.LogError($"AutoHandXriUiBridge: missing actions {mapName}/{performedName} or {valueName}.");
            return;
        }

        reader.inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference;
        reader.inputActionReferencePerformed = InputActionReference.Create(performed);
        reader.inputActionReferenceValue = InputActionReference.Create(value);
        performed.Enable();
        value.Enable();
    }
}
