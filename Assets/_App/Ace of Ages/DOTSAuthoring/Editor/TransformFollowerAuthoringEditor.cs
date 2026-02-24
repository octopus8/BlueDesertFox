using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
[CustomEditor(typeof(TransformFollowerAuthoring))]
public class TransformFollowerAuthoringEditor : Editor
{
    private SerializedProperty targetTransformProp;
    private SerializedProperty offsetProp;
    private SerializedProperty followRotationProp;
    private SerializedProperty smoothTimeProp;
    
    private bool showHelp = false;
    
    void OnEnable()
    {
        targetTransformProp = serializedObject.FindProperty("targetTransform");
        offsetProp = serializedObject.FindProperty("offset");
        followRotationProp = serializedObject.FindProperty("followRotation");
        smoothTimeProp = serializedObject.FindProperty("smoothTime");
    }
    
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
                "This component makes an entity in a DOTS subscene follow a Transform outside the subscene.\n\n" +
                "• Target Transform: The GameObject to follow (must be outside the subscene)\n" +
                "• Offset: Local position offset from the target\n" +
                "• Follow Rotation: Match the target's rotation\n" +
                "• Smooth Time: 0 = instant, higher = smoother movement",
                MessageType.Info);
        }
        
        EditorGUILayout.Space(5);
        
        // Target Transform with validation
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(targetTransformProp, new GUIContent("Target Transform"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            ValidateTarget();
        }
        
        if (targetTransformProp.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("⚠ No target assigned! This entity won't follow anything.", MessageType.Warning);
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
    
    private void ValidateTarget()
    {
        var target = targetTransformProp.objectReferenceValue as Transform;
        if (target != null)
        {
            var authoring = (TransformFollowerAuthoring)serializedObject.targetObject;
            
            // Check if target is in a subscene (warning - this might not work as expected)
            if (target.GetComponentInParent<Unity.Scenes.SubScene>() != null)
            {
                Debug.LogWarning(
                    "The target Transform appears to be inside a SubScene. " +
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
    
    // Scene view visualization
    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    static void DrawGizmos(TransformFollowerAuthoring follower, GizmoType gizmoType)
    {
        if (follower.targetTransform == null)
            return;
        
        // Draw line from entity to target
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(follower.transform.position, follower.targetTransform.position);
        
        // Draw target position
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(follower.targetTransform.position, 0.3f);
        
        // Draw final position (target + offset)
        Vector3 finalPos = follower.targetTransform.position + follower.offset;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(finalPos, 0.2f);
        Gizmos.DrawLine(follower.targetTransform.position, finalPos);
        
        // Draw label
        UnityEditor.Handles.Label(
            follower.targetTransform.position + Vector3.up * 0.5f,
            $"Following: {follower.targetTransform.name}\nOffset: {follower.offset}",
            new GUIStyle() { normal = new GUIStyleState() { textColor = Color.white } }
        );
    }
}
#endif

