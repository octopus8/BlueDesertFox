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

    /// <summary>
    /// Creates the <c>Terrain-SlopeBlend.mat</c> asset using the slope-blend shader with default
    /// snow (flat) and concrete (steep) albedo textures.
    /// Accessible via <c>Tools → Terrain → Create Slope Blend Material</c>.
    /// </summary>
    [MenuItem("Tools/Terrain/Create Slope Blend Material")]
    public static void CreateSlopeBlendMaterialMenuItem()
    {
        CreateSlopeBlendMaterial();
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

    /// <summary>Creates the slope-blend terrain material at <c>Assets/_App/Escape Mountain/AppResources/Terrain-SlopeBlend.mat</c> if it does not already exist.</summary>
    private static void CreateSlopeBlendMaterial()
    {
        const string assetPath = "Assets/_App/Escape Mountain/AppResources/Terrain-SlopeBlend.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null)
        {
            EditorGUIUtility.PingObject(existing);
            return;
        }

        Shader slopeShader = Shader.Find("AceOfAges/TerrainSlopeBlend");
        if (slopeShader == null)
        {
            Debug.LogError("[TerrainMaterialCreator] Failed to find 'AceOfAges/TerrainSlopeBlend' shader!");
            return;
        }

        string appResourcesPath = "Assets/_App/Escape Mountain/AppResources";
        if (!AssetDatabase.IsValidFolder(appResourcesPath))
        {
            Debug.LogError("[TerrainMaterialCreator] AppResources folder not found at " + appResourcesPath);
            return;
        }

        var material = new Material(slopeShader);
        material.name = "Terrain-SlopeBlend";

        var flatTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_App/Escape Mountain/AppResources/snow00.png");
        var steepTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_App/Escape Mountain/AppResources/Concrete 0196.jpg");
        if (flatTexture != null)
            material.SetTexture("_FlatMap", flatTexture);
        if (steepTexture != null)
            material.SetTexture("_SteepMap", steepTexture);

        material.SetFloat("_FlatTiling", 0.05f);
        material.SetFloat("_SteepTiling", 0.2f);
        material.SetFloat("_SlopeStart", 0.35f);
        material.SetFloat("_SlopeEnd", 0.55f);
        material.SetFloat("_Smoothness", 0.2f);
        material.SetFloat("_Metallic", 0f);

        AssetDatabase.CreateAsset(material, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(material);
    }
}

