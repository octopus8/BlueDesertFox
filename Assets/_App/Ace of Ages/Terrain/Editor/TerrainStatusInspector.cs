using UnityEngine;
using UnityEditor;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Editor window to check terrain system status and diagnose issues.
/// Open via: Window → Terrain → Status Inspector
/// </summary>
public class TerrainStatusInspector : EditorWindow
{
    private Vector2 _scrollPosition;
    private bool _isPlaying = false;

    /// <summary>
    /// Opens (or focuses) the Terrain Status Inspector editor window.
    /// Accessible via the <c>Window → Terrain → Status Inspector</c> menu.
    /// </summary>
    [MenuItem("Window/Terrain/Status Inspector")]
    public static void ShowWindow()
    {
        GetWindow<TerrainStatusInspector>("Terrain Status");
    }

    /// <summary>Subscribes to the <see cref="EditorApplication.playModeStateChanged"/> event to repaint the window on play mode transitions.</summary>
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    /// <summary>Unsubscribes from the play mode state changed event when the window is closed or disabled.</summary>
    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    /// <summary>Updates <c>_isPlaying</c> and repaints the window when Unity enters or exits play mode.</summary>
    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        _isPlaying = state == PlayModeStateChange.EnteredPlayMode;
        Repaint();
    }

    /// <summary>Draws the entire status inspector UI including material, URP, package, and runtime checks plus action buttons.</summary>
    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        GUILayout.Label("Terrain System Status Inspector", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Check 1: TerrainMaterial exists
        CheckMaterial();
        EditorGUILayout.Space();

        // Check 2: URP configured
        CheckURP();
        EditorGUILayout.Space();

        // Check 3: Entities packages
        CheckPackages();
        EditorGUILayout.Space();

        // Check 4: Play mode status
        if (Application.isPlaying)
        {
            CheckPlayModeStatus();
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play Mode to see runtime terrain status", MessageType.Info);
        }

        EditorGUILayout.Space();

        // Action buttons
        if (GUILayout.Button("Create TerrainMaterial"))
        {
            TerrainMaterialCreator.CreateMaterialMenuItem();
        }

        if (Application.isPlaying)
        {
            if (GUILayout.Button("Force Refresh Stats"))
            {
                Repaint();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>Draws the material check section, showing whether <c>TerrainMaterial</c> exists in the <c>Resources</c> folder.</summary>
    private void CheckMaterial()
    {
        GUILayout.Label("1. TerrainMaterial Check", EditorStyles.boldLabel);

        var material = Resources.Load<Material>("TerrainMaterial");
        if (material != null)
        {
            EditorGUILayout.HelpBox("✓ TerrainMaterial found in Resources", MessageType.Info);
            EditorGUILayout.ObjectField("Material", material, typeof(Material), false);

            if (material.shader != null)
            {
                EditorGUILayout.LabelField("Shader", material.shader.name);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("✗ TerrainMaterial NOT FOUND in Resources folder!\n" +
                "Click 'Create TerrainMaterial' button below.", MessageType.Error);
        }
    }

    /// <summary>Draws the URP check section, verifying that a scriptable render pipeline is configured and the URP Lit shader is available.</summary>
    private void CheckURP()
    {
        GUILayout.Label("2. URP Configuration Check", EditorStyles.boldLabel);

        var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (urpAsset != null)
        {
            EditorGUILayout.HelpBox($"✓ URP Active: {urpAsset.name}", MessageType.Info);
            EditorGUILayout.ObjectField("Pipeline Asset", urpAsset, typeof(UnityEngine.Rendering.RenderPipelineAsset), false);
        }
        else
        {
            EditorGUILayout.HelpBox("✗ No Render Pipeline configured!\n" +
                "Go to: Edit → Project Settings → Graphics\n" +
                "Set 'Scriptable Render Pipeline Settings' to a URP asset.", MessageType.Error);
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            EditorGUILayout.HelpBox("✓ URP Lit shader found", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("✗ URP Lit shader not found!", MessageType.Warning);
        }
    }

    /// <summary>Draws the packages check section, detecting the presence of <c>Unity.Entities</c> and <c>Unity.Rendering.Hybrid</c> assemblies by reflection.</summary>
    private void CheckPackages()
    {
        GUILayout.Label("3. Required Packages Check", EditorStyles.boldLabel);

        // Check if Entities package is present by looking for the namespace
        bool hasEntities = System.Type.GetType("Unity.Entities.World, Unity.Entities") != null;
        bool hasRendering = System.Type.GetType("Unity.Rendering.RenderMeshUtility, Unity.Rendering.Hybrid") != null;

        if (hasEntities)
        {
            EditorGUILayout.HelpBox("✓ Unity.Entities package present", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("✗ Unity.Entities package missing!", MessageType.Error);
        }

        if (hasRendering)
        {
            EditorGUILayout.HelpBox("✓ Unity.Rendering (Entities Graphics) package present", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("✗ Unity.Rendering package missing!", MessageType.Error);
        }
    }

    /// <summary>Draws the runtime status section during play mode, showing tile counts and rendering state from the live ECS world.</summary>
    private void CheckPlayModeStatus()
    {
        GUILayout.Label("4. Runtime Status", EditorStyles.boldLabel);

        if (World.DefaultGameObjectInjectionWorld == null)
        {
            EditorGUILayout.HelpBox("✗ Default ECS World not initialized!", MessageType.Error);
            return;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;

        // Count terrain entities
        var tileQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainTile>());
        int totalTiles = tileQuery.CalculateEntityCount();

        var meshQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<VertexElement>());
        int tilesWithMesh = meshQuery.CalculateEntityCount();

        var renderQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<MaterialMeshInfo>());
        int tilesWithRendering = renderQuery.CalculateEntityCount();

        EditorGUILayout.LabelField("Total Terrain Tiles", totalTiles.ToString());
        EditorGUILayout.LabelField("Tiles with Mesh Data", tilesWithMesh.ToString());
        EditorGUILayout.LabelField("Tiles with Rendering", tilesWithRendering.ToString());

        if (totalTiles == 0)
        {
            EditorGUILayout.HelpBox("No terrain tiles spawned yet.\n" +
                "Check:\n" +
                "- Is 'Player To Track' assigned in TerrainConfigAuthoring?\n" +
                "- Is the player GameObject active and positioned correctly?\n" +
                "- Is TerrainConfigAuthoring in the scene?", MessageType.Warning);
        }
        else if (tilesWithRendering == 0)
        {
            EditorGUILayout.HelpBox($"Tiles spawned ({totalTiles}) but none have rendering!\n" +
                "Check TerrainRenderingSystem for errors.", MessageType.Error);
        }
        else if (tilesWithRendering < totalTiles)
        {
            EditorGUILayout.HelpBox($"Some tiles missing rendering: {tilesWithRendering}/{totalTiles}\n" +
                "This is normal if tiles are still being generated.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox($"✓ All {totalTiles} tiles have rendering set up!", MessageType.Info);
        }

        // Check for test cube
        var testQuery = em.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>());
        bool foundTestCube = false;
        var entities = testQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var entity in entities)
        {
            var name = em.GetName(entity);
            if (name.Contains("TestRenderCube"))
            {
                foundTestCube = true;
                break;
            }
        }
        entities.Dispose();

        if (foundTestCube)
        {
            EditorGUILayout.HelpBox("✓ Test render cube found", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Test cube not found (might have been disabled)", MessageType.Info);
        }
    }

    /// <summary>Triggers a repaint every frame during play mode so the status counters stay live.</summary>
    private void Update()
    {
        if (Application.isPlaying)
        {
            Repaint(); // Refresh every frame in play mode
        }
    }
}


