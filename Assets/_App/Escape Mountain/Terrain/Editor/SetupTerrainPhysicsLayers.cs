using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility for setting up terrain physics layer.
/// Adds "Terrain" layer and configures collision matrix.
/// </summary>
public class SetupTerrainPhysicsLayers : Editor
{
    /// <summary>
    /// Ensures the <c>Terrain</c> physics layer exists in the Project Settings Tag Manager,
    /// adding it to the first available slot if absent.
    /// Accessible via the <c>Tools → Terrain → Setup Physics Layer</c> menu.
    /// </summary>
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
        
        // Display success message
        string message = terrainLayerExists 
            ? $"Terrain layer already exists at index {terrainLayerIndex}."
            : $"Terrain layer created at index {terrainLayerIndex}.";
        
        EditorUtility.DisplayDialog(
            "Setup Terrain Physics Layer",
            message,
            "OK");
        
        Debug.Log($"[TerrainPhysics] Layer setup complete. Terrain layer index: {terrainLayerIndex}");
    }
}

