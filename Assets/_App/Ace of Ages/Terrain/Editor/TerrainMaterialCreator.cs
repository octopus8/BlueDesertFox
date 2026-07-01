using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Editor utility to create the TerrainMaterial if it doesn't exist.
/// This will run automatically when Unity starts or when scripts recompile.
/// </summary>
[InitializeOnLoad]
public static class TerrainMaterialCreator
{
    static TerrainMaterialCreator()
    {
        // Run after scripts compile
        EditorApplication.delayCall += CheckAndCreateMaterial;
    }

    /// <summary>
    /// Creates (or recreates) the <c>TerrainMaterial</c> URP Lit asset at
    /// <c>Assets/Resources/TerrainMaterial.mat</c> with default greenish-gray colour settings
    /// and pings the asset in the Project window when done.
    /// Accessible via the <c>Tools → Terrain → Create Terrain Material</c> menu.
    /// </summary>
    [MenuItem("Tools/Terrain/Create Terrain Material")]
    public static void CreateMaterialMenuItem()
    {
        CreateTerrainMaterial();
    }

    /// <summary>Checks whether <c>TerrainMaterial</c> exists in <c>Resources</c> and calls <see cref="CreateTerrainMaterial"/> if it is missing. Invoked automatically on editor load via <see cref="EditorApplication.delayCall"/>.</summary>
    private static void CheckAndCreateMaterial()
    {
        // Check if material exists in Resources
        var material = Resources.Load<Material>("TerrainMaterial");
        
        if (material == null)
        {
            CreateTerrainMaterial();
        }
    }

    /// <summary>Creates the <c>Assets/Resources/TerrainMaterial.mat</c> URP Lit asset with default greenish-gray albedo and saves it to disk.</summary>
    private static void CreateTerrainMaterial()
    {
        // Ensure Resources folder exists
        string resourcesPath = "Assets/Resources";
        if (!AssetDatabase.IsValidFolder(resourcesPath))
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Resources"));
            AssetDatabase.Refresh();
        }

        // Find URP Lit shader
        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader == null)
        {
            Debug.LogError("[TerrainMaterialCreator] Failed to find 'Universal Render Pipeline/Lit' shader! Is URP installed?");
            return;
        }

        // Create material
        Material terrainMaterial = new Material(urpLitShader);
        terrainMaterial.name = "TerrainMaterial";
        
        // Set some default properties for visibility
        terrainMaterial.SetColor("_BaseColor", new Color(0.4f, 0.6f, 0.3f, 1f)); // Greenish-gray
        terrainMaterial.SetFloat("_Smoothness", 0.2f); // Slightly rough
        terrainMaterial.SetFloat("_Metallic", 0f); // Not metallic
        
        // Use white texture for base map (solid color)
        terrainMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
        
        // Save to Resources folder
        string assetPath = "Assets/Resources/TerrainMaterial.mat";
        AssetDatabase.CreateAsset(terrainMaterial, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        EditorGUIUtility.PingObject(terrainMaterial);
    }
}

