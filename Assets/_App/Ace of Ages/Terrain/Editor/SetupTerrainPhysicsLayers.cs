using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility for setting up terrain physics layers.
/// Adds "Terrain" and "TerrainLowDetail" layers and configures collision matrix.
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
        
        // Find or add "Terrain" layer (for close terrain)
        int closeTerrainLayerIndex = -1;
        bool closeTerrainLayerExists = false;
        
        for (int i = 8; i < 32; i++) // Layers 0-7 are reserved
        {
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);
            string layerName = layerProp.stringValue;
            
            if (layerName == "Terrain")
            {
                closeTerrainLayerIndex = i;
                closeTerrainLayerExists = true;
                break;
            }
            
            // Find first empty slot
            if (closeTerrainLayerIndex == -1 && string.IsNullOrEmpty(layerName))
            {
                closeTerrainLayerIndex = i;
            }
        }
        
        // Add "Terrain" layer if it doesn't exist
        if (!closeTerrainLayerExists)
        {
            if (closeTerrainLayerIndex == -1)
            {
                EditorUtility.DisplayDialog(
                    "Setup Terrain Physics Layers",
                    "No available layer slots found! All 32 layers are in use.",
                    "OK");
                return;
            }
            
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(closeTerrainLayerIndex);
            layerProp.stringValue = "Terrain";
            tagManager.ApplyModifiedProperties();
        }
        
        // Find or add "TerrainLowDetail" layer
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
        
        // Add "TerrainLowDetail" layer if it doesn't exist
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
        // Both Terrain and TerrainLowDetail should collide with player but not with grabbable objects
        
        // Find Grabbable layer index (from AutoHand)
        int grabbableLayerIndex = LayerMask.NameToLayer("Grabbable");
        
        if (grabbableLayerIndex != -1 && closeTerrainLayerIndex != -1)
        {
            // Disable collision between Terrain and Grabbable
            Physics.IgnoreLayerCollision(closeTerrainLayerIndex, grabbableLayerIndex, true);
        }
        
        if (grabbableLayerIndex != -1 && terrainLayerIndex != -1)
        {
            // Disable collision between TerrainLowDetail and Grabbable
            Physics.IgnoreLayerCollision(terrainLayerIndex, grabbableLayerIndex, true);
        }
        
        // Display success message
        string closeTerrainMessage = closeTerrainLayerExists 
            ? $"Terrain layer already exists at index {closeTerrainLayerIndex}."
            : $"Terrain layer created at index {closeTerrainLayerIndex}.";
        
        string lowDetailMessage = layerExists 
            ? $"TerrainLowDetail layer already exists at index {terrainLayerIndex}."
            : $"TerrainLowDetail layer created at index {terrainLayerIndex}.";
        
        string message = $"{closeTerrainMessage}\n{lowDetailMessage}\n\nCollision matrix configured:\n- Disabled collision with Grabbable layer\n- Enabled collision with all other layers";
        
        EditorUtility.DisplayDialog(
            "Setup Terrain Physics Layers",
            message,
            "OK");
        
        Debug.Log($"[TerrainPhysics] Layer setup complete. Terrain layer index: {closeTerrainLayerIndex}, TerrainLowDetail layer index: {terrainLayerIndex}");
    }
}

