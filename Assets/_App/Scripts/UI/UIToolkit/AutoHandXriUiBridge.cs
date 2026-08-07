using Autohand;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Keeps AutoHand hand meshes, disables AutoHand uGUI lasers, and attaches XRI UI rays
/// under AutoHand Controller (left)/(right) for UI Toolkit world-space panels.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class AutoHandXriUiBridge : MonoBehaviour
{
    const string LeftControllerName = "Controller (left)";
    const string RightControllerName = "Controller (right)";
    const string LeftInteractionMap = "XRI Left Interaction";
    const string RightInteractionMap = "XRI Right Interaction";

    [SerializeField] GameObject rayInteractorPrefab;
    [SerializeField] InputActionAsset xriInputActions;

    void Awake()
    {
        if (xriInputActions != null)
            xriInputActions.Enable();

        DisableHandCanvasPointers();
        EnsureInteractionManager();
        SetupRay(LeftControllerName, LeftInteractionMap, InteractorHandedness.Left);
        SetupRay(RightControllerName, RightInteractionMap, InteractorHandedness.Right);
    }

    static void DisableHandCanvasPointers()
    {
        HandCanvasPointer[] pointers = FindObjectsByType<HandCanvasPointer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
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

    void SetupRay(string controllerName, string actionMapName, InteractorHandedness handedness)
    {
        if (rayInteractorPrefab == null || xriInputActions == null)
        {
            Debug.LogError("AutoHandXriUiBridge: rayInteractorPrefab or xriInputActions is not assigned.");
            return;
        }

        Transform controller = FindNamedChild(controllerName);
        if (controller == null)
        {
            Debug.LogError($"AutoHandXriUiBridge: '{controllerName}' not found.");
            return;
        }

        GameObject rayGo = Instantiate(rayInteractorPrefab, controller);
        rayGo.name = $"XRI UI Ray ({handedness})";
        rayGo.transform.localPosition = Vector3.zero;
        rayGo.transform.localRotation = Quaternion.identity;

        XRRayInteractor ray = rayGo.GetComponent<XRRayInteractor>();
        if (ray == null)
        {
            Debug.LogError("AutoHandXriUiBridge: Ray Interactor prefab missing XRRayInteractor.");
            return;
        }

        ray.handedness = handedness;
        ray.enableUIInteraction = true;
        WireButton(ray.selectInput, actionMapName, "Select", "Select Value");
        WireButton(ray.uiPressInput, actionMapName, "UI Press", "UI Press Value");
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

    static Transform FindNamedChild(string objectName)
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == objectName)
                return all[i];
        }

        return null;
    }
}
