using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// Custom Inspector for <see cref="TransformFollowerAuthoring"/> that provides a context-sensitive UI
/// showing only the fields relevant to the selected <see cref="TransformFollowerAuthoring.TargetMode"/>,
/// inline validation hints, quick-find buttons, and Scene-view Gizmos for the follow offset.
/// </summary>
[CustomEditor(typeof(TransformFollowerAuthoring))]
public class TransformFollowerAuthoringEditor : Editor
{
    private SerializedProperty targetModeProp;
    private SerializedProperty targetNameProp;
    private SerializedProperty targetTagProp;
    private SerializedProperty targetGameObjectProp;
    private SerializedProperty offsetProp;
    private SerializedProperty followRotationProp;
    private SerializedProperty smoothTimeProp;
    
    private bool showHelp = false;
    
    /// <summary>Caches all <see cref="SerializedProperty"/> references needed to draw the custom inspector.</summary>
    void OnEnable()
    {
        targetModeProp = serializedObject.FindProperty("targetMode");
        targetNameProp = serializedObject.FindProperty("targetName");
        targetTagProp = serializedObject.FindProperty("targetTag");
        targetGameObjectProp = serializedObject.FindProperty("targetGameObject");
        offsetProp = serializedObject.FindProperty("offset");
        followRotationProp = serializedObject.FindProperty("followRotation");
        smoothTimeProp = serializedObject.FindProperty("smoothTime");
    }
    
    /// <summary>
    /// Draws the custom inspector UI, showing the target mode selector and the matching input
    /// field (name, tag, or direct reference), together with validation HelpBoxes, quick-find
    /// buttons, offset preset shortcuts, smooth time presets, and a play-mode status indicator.
    /// </summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        EditorGUILayout.Space(5);
        
        // Header with help toggle
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Transform Follower Settings", EditorStyles.boldLabel);
        showHelp = GUILayout.Toggle(showHelp, "?", "Button", GUILayout.Width(25));
        EditorGUILayout.EndHorizontal();
        
        if (showHelp)
        {
            EditorGUILayout.HelpBox(
                "This component makes an entity in a DOTS subscene follow a GameObject outside the subscene.\n\n" +
                "TARGET MODES:\n" +
                "• Find By Name: Enter the exact GameObject name (e.g., 'Right Controller')\n" +
                "• Find By Tag: Use a tag to find the target (e.g., 'Player')\n" +
                "• Direct Reference: Drag GameObject (only works for objects in same subscene)\n\n" +
                "RECOMMENDED: Use 'Find By Name' for objects outside the subscene!",
                MessageType.Info);
        }
        
        EditorGUILayout.Space(5);
        
        // Target Mode dropdown
        EditorGUILayout.PropertyField(targetModeProp, new GUIContent("Target Mode"));
        
        EditorGUILayout.Space(3);
        
        // Show appropriate field based on mode
        TransformFollowerAuthoring.TargetMode mode = (TransformFollowerAuthoring.TargetMode)targetModeProp.enumValueIndex;
        
        switch (mode)
        {
            case TransformFollowerAuthoring.TargetMode.FindByName:
                EditorGUILayout.PropertyField(targetNameProp, new GUIContent("Target Name"));
                if (string.IsNullOrEmpty(targetNameProp.stringValue))
                {
                    EditorGUILayout.HelpBox("⚠ Enter the exact name of the GameObject (e.g., 'Right Controller')", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox($"✓ Will find GameObject named: '{targetNameProp.stringValue}' at runtime", MessageType.Info);
                }
                
                // Quick find button
                if (GUILayout.Button("Find in Scene"))
                {
                    var found = GameObject.Find(targetNameProp.stringValue);
                    if (found != null)
                    {
                        EditorGUIUtility.PingObject(found);
                        Debug.Log($"Found '{targetNameProp.stringValue}' in scene!", found);
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find GameObject named '{targetNameProp.stringValue}' in scene.");
                    }
                }
                break;
                
            case TransformFollowerAuthoring.TargetMode.FindByTag:
                EditorGUILayout.PropertyField(targetTagProp, new GUIContent("Target Tag"));
                if (string.IsNullOrEmpty(targetTagProp.stringValue))
                {
                    EditorGUILayout.HelpBox("⚠ Enter a tag (e.g., 'Player', 'MainCamera')", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox($"✓ Will find GameObject with tag: '{targetTagProp.stringValue}' at runtime", MessageType.Info);
                }
                
                // Quick find button
                if (GUILayout.Button("Find in Scene"))
                {
                    try
                    {
                        var found = GameObject.FindGameObjectWithTag(targetTagProp.stringValue);
                        if (found != null)
                        {
                            EditorGUIUtility.PingObject(found);
                            Debug.Log($"Found GameObject with tag '{targetTagProp.stringValue}': {found.name}", found);
                        }
                    }
                    catch
                    {
                        Debug.LogWarning($"Tag '{targetTagProp.stringValue}' is not defined in Tag Manager.");
                    }
                }
                break;
                
            case TransformFollowerAuthoring.TargetMode.DirectReference:
                EditorGUILayout.PropertyField(targetGameObjectProp, new GUIContent("Target GameObject"));
                if (targetGameObjectProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("⚠ Drag a GameObject here (must be in same subscene)", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("⚠ Direct reference only works for objects in the SAME subscene. For objects outside, use 'Find By Name'.", MessageType.Warning);
                }
                break;
        }
        
        EditorGUILayout.Space(3);
        
        // Offset with preset buttons
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(offsetProp, new GUIContent("Offset"));
        EditorGUILayout.EndHorizontal();
        
        // Offset presets
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Above", GUILayout.Width(60)))
            offsetProp.vector3Value = new Vector3(0, 2, 0);
        if (GUILayout.Button("Behind", GUILayout.Width(60)))
            offsetProp.vector3Value = new Vector3(0, 0, -2);
        if (GUILayout.Button("Front", GUILayout.Width(60)))
            offsetProp.vector3Value = new Vector3(0, 0, 2);
        if (GUILayout.Button("Reset", GUILayout.Width(60)))
            offsetProp.vector3Value = Vector3.zero;
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(3);
        
        // Follow Rotation
        EditorGUILayout.PropertyField(followRotationProp, new GUIContent("Follow Rotation"));
        
        EditorGUILayout.Space(3);
        
        // Smooth Time with presets
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(smoothTimeProp, new GUIContent("Smooth Time"));
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Instant (0)", GUILayout.Width(80)))
            smoothTimeProp.floatValue = 0f;
        if (GUILayout.Button("Fast (0.05)", GUILayout.Width(80)))
            smoothTimeProp.floatValue = 0.05f;
        if (GUILayout.Button("Smooth (0.2)", GUILayout.Width(80)))
            smoothTimeProp.floatValue = 0.2f;
        if (GUILayout.Button("Slow (0.5)", GUILayout.Width(80)))
            smoothTimeProp.floatValue = 0.5f;
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(10);
        
        // Performance info
        if (smoothTimeProp.floatValue > 0)
        {
            EditorGUILayout.HelpBox("ℹ Smoothing enabled. Entity will lag slightly behind target.", MessageType.Info);
        }
        
        serializedObject.ApplyModifiedProperties();
        
        // Scene view visualization button
        EditorGUILayout.Space(5);
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("✓ Playing - Entity should be following target now", MessageType.None);
        }
    }
    
    /// <summary>Checks whether the configured direct-reference target is inside a SubScene and warns if the authoring component itself is not in a SubScene, since cross-boundary references don't work at runtime.</summary>
    private void ValidateTarget()
    {
        var targetGO = targetGameObjectProp.objectReferenceValue as GameObject;
        if (targetGO != null)
        {
            var authoring = (TransformFollowerAuthoring)serializedObject.targetObject;
            
            // Check if target is in a subscene (warning - this might not work as expected)
            if (targetGO.GetComponentInParent<Unity.Scenes.SubScene>() != null)
            {
                Debug.LogWarning(
                    "The target GameObject appears to be inside a SubScene. " +
                    "This may not work as expected. The target should typically be outside the subscene.", 
                    authoring);
            }
            
            // Check if authoring is in a subscene
            if (authoring.GetComponentInParent<Unity.Scenes.SubScene>() == null)
            {
                Debug.LogWarning(
                    "TransformFollowerAuthoring should be on an entity INSIDE a SubScene. " +
                    "It appears this GameObject is not in a subscene.", 
                    authoring);
            }
        }
    }
    
    /// <summary>Draws Scene-view gizmos showing the resolved target position, the follower's current position, and the offset when the component is selected or active.</summary>
    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    static void DrawGizmos(TransformFollowerAuthoring follower, GizmoType gizmoType)
    {
        Transform targetTransform = null;
        
        // Try to get the target based on mode
        switch (follower.targetMode)
        {
            case TransformFollowerAuthoring.TargetMode.FindByName:
                if (!string.IsNullOrEmpty(follower.targetName))
                {
                    var go = GameObject.Find(follower.targetName);
                    if (go != null) targetTransform = go.transform;
                }
                break;
                
            case TransformFollowerAuthoring.TargetMode.FindByTag:
                if (!string.IsNullOrEmpty(follower.targetTag))
                {
                    try
                    {
                        var go = GameObject.FindGameObjectWithTag(follower.targetTag);
                        if (go != null) targetTransform = go.transform;
                    }
                    catch { }
                }
                break;
                
            case TransformFollowerAuthoring.TargetMode.DirectReference:
                if (follower.targetGameObject != null)
                    targetTransform = follower.targetGameObject.transform;
                break;
        }
        
        if (targetTransform == null)
            return;
        
        // Draw line from entity to target
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(follower.transform.position, targetTransform.position);
        
        // Draw target position
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(targetTransform.position, 0.3f);
        
        // Draw final position (target + offset)
        Vector3 finalPos = targetTransform.position + follower.offset;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(finalPos, 0.2f);
        Gizmos.DrawLine(targetTransform.position, finalPos);
        
        // Draw label
        string targetInfo = follower.targetMode == TransformFollowerAuthoring.TargetMode.FindByName 
            ? follower.targetName 
            : follower.targetMode == TransformFollowerAuthoring.TargetMode.FindByTag 
                ? $"Tag:{follower.targetTag}" 
                : targetTransform.gameObject.name;
        
        Handles.Label(
            targetTransform.position + Vector3.up * 0.5f,
            $"Following: {targetInfo}\nOffset: {follower.offset}",
            new GUIStyle() { normal = new GUIStyleState() { textColor = Color.white } }
        );
    }
}
#endif

