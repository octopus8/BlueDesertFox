using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility for setting up terrain physics layers.
/// Adds "TerrainLowDetail" layer and configures collision matrix.
/// </summary>
public class SetupTerrainPhysicsLayers : Editor
{
    [MenuItem("Tools/Terrain/Setup Physics Layers")]
    public static void SetupPhysicsLayers()
    {
        // Get TagManager
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        
        SerializedProperty layersProp = tagManager.FindProperty("layers");
        
        // Find "TerrainLowDetail" layer or add it
        int terrainLayerIndex = -1;
        bool layerExists = false;
        
        for (int i = 8; i < 32; i++) // Layers 0-7 are reserved
        {
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);
            string layerName = layerProp.stringValue;
            
            if (layerName == "TerrainLowDetail")
            {
                terrainLayerIndex = i;
                layerExists = true;
                break;
            }
            
            // Find first empty slot
            if (terrainLayerIndex == -1 && string.IsNullOrEmpty(layerName))
            {
                terrainLayerIndex = i;
            }
        }
        
        // Add layer if it doesn't exist
        if (!layerExists)
        {
            if (terrainLayerIndex == -1)
            {
                EditorUtility.DisplayDialog(
                    "Setup Terrain Physics Layers",
                    "No available layer slots found! All 32 layers are in use.",
                    "OK");
                return;
            }
            
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(terrainLayerIndex);
            layerProp.stringValue = "TerrainLowDetail";
            tagManager.ApplyModifiedProperties();
        }
        
        // Configure physics layer collision matrix
        // TerrainLowDetail should collide with player but not with grabbable objects
        
        // Find Grabbable layer index (from AutoHand)
        int grabbableLayerIndex = LayerMask.NameToLayer("Grabbable");
        
        if (grabbableLayerIndex != -1 && terrainLayerIndex != -1)
        {
            // Disable collision between TerrainLowDetail and Grabbable
            Physics.IgnoreLayerCollision(terrainLayerIndex, grabbableLayerIndex, true);
        }
        
        // Display success message
        string message = layerExists 
            ? $"TerrainLowDetail layer already exists at index {terrainLayerIndex}.\n\nCollision matrix updated."
            : $"TerrainLowDetail layer created at index {terrainLayerIndex}.\n\nCollision matrix configured:\n- Disabled collision with Grabbable layer\n- Enabled collision with all other layers";
        
        EditorUtility.DisplayDialog(
            "Setup Terrain Physics Layers",
            message,
            "OK");
        
        Debug.Log($"[TerrainPhysics] Layer setup complete. TerrainLowDetail layer index: {terrainLayerIndex}");
    }
}

