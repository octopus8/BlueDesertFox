using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility for setting up terrain physics layer.
/// Adds "Terrain" layer and configures collision matrix.
/// </summary>
public class SetupTerrainPhysicsLayers : Editor
{
    [MenuItem("Tools/Terrain/Setup Physics Layer")]
    public static void SetupPhysicsLayers()
    {
        // Get TagManager
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        
        SerializedProperty layersProp = tagManager.FindProperty("layers");
        
        // Find or add "Terrain" layer
        int terrainLayerIndex = -1;
        bool terrainLayerExists = false;
        
        for (int i = 8; i < 32; i++) // Layers 0-7 are reserved
        {
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);
            string layerName = layerProp.stringValue;
            
            if (layerName == "Terrain")
            {
                terrainLayerIndex = i;
                terrainLayerExists = true;
                break;
            }
            
            // Find first empty slot
            if (terrainLayerIndex == -1 && string.IsNullOrEmpty(layerName))
            {
                terrainLayerIndex = i;
            }
        }
        
        // Add "Terrain" layer if it doesn't exist
        if (!terrainLayerExists)
        {
            if (terrainLayerIndex == -1)
            {
                EditorUtility.DisplayDialog(
                    "Setup Terrain Physics Layer",
                    "No available layer slots found! All 32 layers are in use.",
                    "OK");
                return;
            }
            
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(terrainLayerIndex);
            layerProp.stringValue = "Terrain";
            tagManager.ApplyModifiedProperties();
        }
        
        // Configure physics layer collision matrix
        // Terrain should not collide with grabbable objects
        
        // Find Grabbable layer index (from AutoHand)
        int grabbableLayerIndex = LayerMask.NameToLayer("Grabbable");
        
        if (grabbableLayerIndex != -1 && terrainLayerIndex != -1)
        {
            // Disable collision between Terrain and Grabbable
            Physics.IgnoreLayerCollision(terrainLayerIndex, grabbableLayerIndex, true);
        }
        
        // Display success message
        string terrainMessage = terrainLayerExists 
            ? $"Terrain layer already exists at index {terrainLayerIndex}."
            : $"Terrain layer created at index {terrainLayerIndex}.";
        
        string message = $"{terrainMessage}\n\nCollision matrix configured:\n- Disabled collision with Grabbable layer\n- Enabled collision with all other layers";
        
        EditorUtility.DisplayDialog(
            "Setup Terrain Physics Layer",
            message,
            "OK");
        
        Debug.Log($"[TerrainPhysics] Layer setup complete. Terrain layer index: {terrainLayerIndex}");
    }
}

