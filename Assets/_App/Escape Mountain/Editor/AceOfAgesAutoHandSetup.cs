using System.Linq;
using Autohand;
using Autohand.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility to graft AutoHand physics hands onto XRI Hands rigs,
/// bake optical hand-tracking bridges onto scene instances, and fix ECS references.
/// </summary>
public static class AceOfAgesAutoHandSetup
{
    const string AceOfAgesMainScenePath = "Assets/_App/Ace of Ages/Ace of Ages.unity";
    const string AceOfAgesSubScenePath = "Assets/_App/Ace of Ages/Entities Subscene.unity";
    const string EscapeMountainScenePath = "Assets/_App/Escape Mountain/Escape Mountain.unity";

    const string RobotHandLeftPath = "Assets/AutoHand/Examples/Scenes/OpenXR/Prefabs/RobotHand (OpenXR)(L).prefab";
    const string RobotHandRightPath = "Assets/AutoHand/Examples/Scenes/OpenXR/Prefabs/RobotHand (OpenXR)(R).prefab";
    const string LeftHandTrackingPath = "Assets/Samples/XR Hands/1.8.0/HandVisualizer/Prefabs/Left Hand Tracking.prefab";
    const string RightHandTrackingPath = "Assets/Samples/XR Hands/1.8.0/HandVisualizer/Prefabs/Right Hand Tracking.prefab";

    const string XrRigName = "XR Origin Hands (XR Rig)";
    const string HandTrackingParentName = "Hand Tracking";
    const string LeftHandName = "RobotHand (L)";
    const string RightHandName = "RobotHand (R)";
    const string LeftHandTrackingName = "Left Hand Tracking";
    const string RightHandTrackingName = "Right Hand Tracking";

    [MenuItem("Tools/Ace of Ages/Integrate AutoHand Hands")]
    public static void IntegrateFromMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "Integrate AutoHand Hands",
                "This will modify Ace of Ages.unity and Entities Subscene.unity. Continue?",
                "Integrate",
                "Cancel"))
            return;

        IntegrateAceOfAges();
    }

    [MenuItem("Tools/Escape Mountain/Bake AutoHand Hand Tracking")]
    public static void BakeEscapeMountainFromMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "Bake AutoHand Hand Tracking",
                "This will bake AutoHand optical hand-tracking bridges into Escape Mountain.unity " +
                "on the existing Left/Right Hand Tracking instances (and clean leftover bootstrap components). Continue?",
                "Bake",
                "Cancel"))
            return;

        BakeEscapeMountain();
    }

    /// <summary>Entry point for Unity batch mode: -executeMethod AceOfAgesAutoHandSetup.ExecuteFromBatch</summary>
    public static void ExecuteFromBatch()
    {
        IntegrateAceOfAges();
        EditorApplication.Exit(0);
    }

    /// <summary>Entry point for Unity batch mode: -executeMethod AceOfAgesAutoHandSetup.ExecuteEscapeMountainBakeFromBatch</summary>
    public static void ExecuteEscapeMountainBakeFromBatch()
    {
        BakeEscapeMountain();
        EditorApplication.Exit(0);
    }

    /// <summary>Bake Escape Mountain AutoHand tracking without quitting the editor.</summary>
    public static void BakeEscapeMountainInEditor()
    {
        BakeEscapeMountain();
    }

    static void IntegrateAceOfAges()
    {
        var mainScene = EditorSceneManager.OpenScene(AceOfAgesMainScenePath, OpenSceneMode.Single);
        SetupAceOfAgesMainScene(mainScene);
        EditorSceneManager.SaveScene(mainScene);

        var subScene = EditorSceneManager.OpenScene(AceOfAgesSubScenePath, OpenSceneMode.Single);
        SetupAceOfAgesSubScene(subScene);
        EditorSceneManager.SaveScene(subScene);

        AssetDatabase.SaveAssets();
        Debug.Log("[AceOfAgesAutoHandSetup] AutoHand integration complete.");
    }

    static void BakeEscapeMountain()
    {
        var scene = EditorSceneManager.OpenScene(EscapeMountainScenePath, OpenSceneMode.Single);

        var xrOrigin = FindRootObject(scene, XrRigName);
        if (xrOrigin == null)
        {
            // Escape Mountain nests XR Origin under DeviceTracking / prefab hierarchy.
            xrOrigin = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == XrRigName)?.gameObject;
        }

        if (xrOrigin == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] Could not find '{XrRigName}' in Escape Mountain.");
            return;
        }

        DisableXriConflicts(xrOrigin.transform);

        var cameraOffset = FindChildRecursive(xrOrigin.transform, "Camera Offset");
        var leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        var rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");

        if (cameraOffset == null || leftController == null || rightController == null)
        {
            Debug.LogError("[AceOfAgesAutoHandSetup] Escape Mountain XR rig is missing Camera Offset or controllers.");
            return;
        }

        var leftOffset = GetOrCreateFollowOffset(leftController, "AutoHand Follow Offset (L)",
            new Vector3(-0.05f, 0f, 0f), Quaternion.Euler(16f, -8f, 8f));
        var rightOffset = GetOrCreateFollowOffset(rightController, "AutoHand Follow Offset (R)",
            new Vector3(0.05f, 0f, 0f), Quaternion.Euler(16f, 8f, 8f));

        var leftHand = FindNamedChild(cameraOffset, LeftHandName);
        var rightHand = FindNamedChild(cameraOffset, RightHandName);
        if (leftHand == null || rightHand == null)
        {
            Debug.LogError("[AceOfAgesAutoHandSetup] Escape Mountain is missing RobotHand instances. " +
                "Run full AutoHand integration before baking hand tracking.");
            return;
        }

        WireHand(leftHand, leftOffset, isLeft: true);
        WireHand(rightHand, rightOffset, isLeft: false);

        BakeHandTrackingSide(xrOrigin.transform, leftHand, LeftHandTrackingName, isLeft: true);
        BakeHandTrackingSide(xrOrigin.transform, rightHand, RightHandTrackingName, isLeft: false);

        RemoveBootstrapComponent(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[AceOfAgesAutoHandSetup] Escape Mountain AutoHand hand tracking bake complete.");
    }

    static void BakeHandTrackingSide(Transform xrOrigin, GameObject robotHand, string trackingInstanceName, bool isLeft)
    {
        if (robotHand == null)
            return;

        var hand = robotHand.GetComponentInChildren<Hand>(true);
        if (hand == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] No Hand on {robotHand.name} for hand tracking.");
            return;
        }

        var handTrackingParent = FindChildRecursive(xrOrigin, HandTrackingParentName);
        if (handTrackingParent == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] Missing '{HandTrackingParentName}' under XR Origin.");
            return;
        }

        var trackingTransform = FindNamedChild(handTrackingParent, trackingInstanceName)?.transform
            ?? FindChildRecursive(handTrackingParent, trackingInstanceName);
        if (trackingTransform == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] Missing '{trackingInstanceName}' under Hand Tracking.");
            return;
        }

        var trackingObject = trackingTransform.gameObject;
        trackingObject.SetActive(false);

        ClearStalePrefabPropertyOverrides(trackingObject);

        var controllerLink = robotHand.GetComponentInChildren<OpenXRHandControllerLink>(true);
        AutoHandTrackingSetup.EnsureHandTracking(trackingObject, hand, controllerLink, isLeft);

        HideHandTrackingVisuals(trackingObject);
        DisableXrHandMeshOnAcquire(trackingObject);

        trackingObject.SetActive(true);
        PrefabUtility.RecordPrefabInstancePropertyModifications(trackingObject);
        EditorUtility.SetDirty(trackingObject);
    }

    static void ClearStalePrefabPropertyOverrides(GameObject instanceRoot)
    {
        if (!PrefabUtility.IsPartOfPrefabInstance(instanceRoot))
            return;

        var mods = PrefabUtility.GetPropertyModifications(instanceRoot);
        if (mods == null || mods.Length == 0)
            return;

        var cleaned = mods.Where(m => m.target != null).ToArray();
        if (cleaned.Length == mods.Length)
            return;

        PrefabUtility.SetPropertyModifications(instanceRoot, cleaned);
    }

    static void DisableXrHandMeshOnAcquire(GameObject trackingObject)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public;

        foreach (var behaviour in trackingObject.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "XRHandMeshController")
                continue;

            var type = behaviour.GetType();
            type.GetField("m_ShowMeshWhenTrackingIsAcquired", flags)?.SetValue(behaviour, false);
            type.GetField("m_HideMeshWhenTrackingIsLost", flags)?.SetValue(behaviour, true);
            EditorUtility.SetDirty(behaviour);
        }
    }

    static void RemoveBootstrapComponent(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
    }

    static void SetupAceOfAgesMainScene(Scene scene)
    {
        RemoveDemoObjects(scene);

        var xrOrigin = FindRootObject(scene, XrRigName);
        if (xrOrigin == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] Could not find '{XrRigName}' in main scene.");
            return;
        }

        DisableXriConflicts(xrOrigin.transform);

        var cameraOffset = FindChildRecursive(xrOrigin.transform, "Camera Offset");
        var leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        var rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");
        var mainCamera = FindChildRecursive(xrOrigin.transform, "Main Camera");

        if (cameraOffset == null || leftController == null || rightController == null || mainCamera == null)
        {
            Debug.LogError("[AceOfAgesAutoHandSetup] XR rig is missing Camera Offset or controllers.");
            return;
        }

        var leftOffset = GetOrCreateFollowOffset(leftController, "AutoHand Follow Offset (L)",
            new Vector3(-0.05f, 0f, 0f), Quaternion.Euler(16f, -8f, 8f));
        var rightOffset = GetOrCreateFollowOffset(rightController, "AutoHand Follow Offset (R)",
            new Vector3(0.05f, 0f, 0f), Quaternion.Euler(16f, 8f, 8f));

        var leftHand = GetOrCreateRobotHand(cameraOffset, RobotHandLeftPath, LeftHandName);
        var rightHand = GetOrCreateRobotHand(cameraOffset, RobotHandRightPath, RightHandName);

        WireHand(leftHand, leftOffset, isLeft: true);
        WireHand(rightHand, rightOffset, isLeft: false);

        WireHandTracking(xrOrigin.transform, leftHand, rightHand);

        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void SetupAceOfAgesSubScene(Scene scene)
    {
        foreach (var terrainConfig in Object.FindObjectsByType<TerrainConfigAuthoring>(FindObjectsSortMode.None))
        {
            terrainConfig.playerSearchMode = TerrainConfigAuthoring.PlayerSearchMode.FindMainCamera;
            EditorUtility.SetDirty(terrainConfig);
        }

        foreach (var follower in Object.FindObjectsByType<TransformFollowerAuthoring>(FindObjectsSortMode.None))
        {
            if (follower.gameObject.name == "PlayerShip")
            {
                follower.targetMode = TransformFollowerAuthoring.TargetMode.FindByName;
                follower.targetName = RightHandName;
                EditorUtility.SetDirty(follower);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void RemoveDemoObjects(Scene scene)
    {
        DestroyRootObject(scene, "Environment");
        DestroyRootObject(scene, "EventSystem");
    }

    static void DestroyRootObject(Scene scene, string objectName)
    {
        var rootObjects = scene.GetRootGameObjects();
        foreach (var root in rootObjects)
        {
            if (root.name != objectName)
                continue;

            Object.DestroyImmediate(root);
            return;
        }
    }

    static void DisableXriConflicts(Transform xrOrigin)
    {
        SetActiveRecursive(xrOrigin, "Locomotion", false);
        SetActiveRecursive(xrOrigin, "Left Hand", false);
        SetActiveRecursive(xrOrigin, "Right Hand", false);
        SetActiveRecursive(xrOrigin, "Hand Visualizer", false);
        DisableHandVisualizerMeshes(xrOrigin);
    }

    static void DisableHandVisualizerMeshes(Transform xrOrigin)
    {
        foreach (var behaviour in xrOrigin.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "HandVisualizer")
                continue;

            var drawMeshesField = behaviour.GetType().GetField("m_DrawMeshes",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            drawMeshesField?.SetValue(behaviour, false);
            behaviour.enabled = false;
            EditorUtility.SetDirty(behaviour);
        }
    }

    static void SetActiveRecursive(Transform root, string objectName, bool active)
    {
        var target = FindChildRecursive(root, objectName);
        if (target != null)
            target.gameObject.SetActive(active);
    }

    static Transform GetOrCreateFollowOffset(Transform parent, string name, Vector3 localPos, Quaternion localRot)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            existing.localPosition = localPos;
            existing.localRotation = localRot;
            return existing;
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create AutoHand Follow Offset");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        return go.transform;
    }

    static GameObject GetOrCreateRobotHand(Transform parent, string prefabPath, string instanceName)
    {
        var existing = FindNamedChild(parent, instanceName);
        if (existing != null)
            return existing;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] Missing prefab: {prefabPath}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(instance, "Add AutoHand RobotHand");
        instance.name = instanceName;
        return instance;
    }

    static void WireHandTracking(Transform xrOrigin, GameObject leftHand, GameObject rightHand)
    {
        var handTrackingParent = GetOrCreateChild(xrOrigin, HandTrackingParentName);
        WireHandTrackingSide(handTrackingParent, leftHand, LeftHandTrackingPath, LeftHandTrackingName, isLeft: true);
        WireHandTrackingSide(handTrackingParent, rightHand, RightHandTrackingPath, RightHandTrackingName, isLeft: false);
    }

    static void WireHandTrackingSide(Transform parent, GameObject robotHand, string prefabPath, string instanceName, bool isLeft)
    {
        if (robotHand == null)
            return;

        var hand = robotHand.GetComponentInChildren<Hand>(true);
        if (hand == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] No Hand on {robotHand.name} for hand tracking.");
            return;
        }

        var trackingObject = GetOrCreatePrefabInstance(parent, prefabPath, instanceName);
        trackingObject.SetActive(false);

        var controllerLink = robotHand.GetComponentInChildren<OpenXRHandControllerLink>(true);
        AutoHandTrackingSetup.EnsureHandTracking(trackingObject, hand, controllerLink, isLeft);

        HideHandTrackingVisuals(trackingObject);
        DisableXrHandMeshOnAcquire(trackingObject);
        trackingObject.SetActive(true);
        PrefabUtility.RecordPrefabInstancePropertyModifications(trackingObject);
        EditorUtility.SetDirty(trackingObject);
    }

    static GameObject GetOrCreatePrefabInstance(Transform parent, string prefabPath, string instanceName)
    {
        var existing = FindNamedChild(parent, instanceName);
        if (existing != null)
            return existing;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] Missing prefab: {prefabPath}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(instance, "Add XR Hand Tracking");
        instance.name = instanceName;
        return instance;
    }

    static void HideHandTrackingVisuals(GameObject trackingObject)
    {
        foreach (Transform child in trackingObject.transform)
        {
            if (child.name.Contains("Visual"))
                child.gameObject.SetActive(false);
        }

        foreach (var renderer in trackingObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.enabled = false;
    }

    static Transform GetOrCreateChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Hand Tracking parent");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void WireHand(GameObject handObject, Transform followTarget, bool isLeft)
    {
        if (handObject == null || followTarget == null)
            return;

        var hand = handObject.GetComponentInChildren<Hand>(true);
        if (hand == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandSetup] No Hand component on {handObject.name}");
            return;
        }

        hand.left = isLeft;
        hand.follow = followTarget;
        SetLayerRecursively(handObject, LayerMask.NameToLayer("Hand"));

        var handFollow = handObject.GetComponentInChildren<HandFollow>(true);
        if (handFollow != null)
            handFollow.maxFollowDistance = 1.5f;

        PrefabUtility.RecordPrefabInstancePropertyModifications(handObject);
        EditorUtility.SetDirty(handObject);
    }

    static GameObject FindRootObject(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
    }

    static GameObject FindNamedChild(Transform parent, string name)
    {
        return parent.Cast<Transform>().FirstOrDefault(t => t.name == name)?.gameObject;
    }

    static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0)
            return;

        foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = layer;
    }
}
