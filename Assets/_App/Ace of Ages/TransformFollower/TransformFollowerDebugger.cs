using Unity.Entities;
using UnityEngine;

/// <summary>
/// Debug helper to diagnose TransformFollower issues.
/// Attach this to any GameObject to get detailed information about the TransformFollower setup.
/// </summary>
public class TransformFollowerDebugger : MonoBehaviour
{
    [Header("Search Settings")]
    [Tooltip("Name of the GameObject to search for")]
    public string searchName = "Right Controller Stabilized Attach";
    
    [Header("Debug Output")]
    [TextArea(10, 20)]
    public string debugOutput = "";
    
    /// <summary>Runs the debug check automatically on scene start.</summary>
    void Start()
    {
        DebugTransformFollowerSetup();
    }
    
    /// <summary>
    /// Queries the ECS world for <see cref="TransformFollowerSettings"/> entities and attempts to
    /// locate the target GameObject named <see cref="searchName"/>, logging a full status report
    /// to both <see cref="debugOutput"/> and the Console. Also accessible via the context menu.
    /// </summary>
    [ContextMenu("Debug TransformFollower Setup")]
    void DebugTransformFollowerSetup()
    {
        debugOutput = "";
        Log("=== TransformFollower Debug Info ===\n");
        
        // 1. Check if the target GameObject exists
        Log($"Searching for GameObject named: '{searchName}'");
        var targetObject = GameObject.Find(searchName);
        if (targetObject == null)
        {
            Log($"ERROR: Could not find GameObject named '{searchName}'!");
            Log("All GameObjects in scene:");
            var allObjects = FindObjectsOfType<GameObject>();
            foreach (var obj in allObjects)
            {
                if (obj.name.Contains("Controller") || obj.name.Contains("Attach"))
                {
                    Log($"  - {obj.name} (path: {GetGameObjectPath(obj)})");
                }
            }
        }
        else
        {
            Log($"SUCCESS: Found target GameObject: {targetObject.name}");
            Log($"  Position: {targetObject.transform.position}");
            Log($"  Rotation: {targetObject.transform.rotation.eulerAngles}");
            Log($"  Active: {targetObject.activeInHierarchy}");
            Log($"  Path: {GetGameObjectPath(targetObject)}");
        }
        
        Log("");
        
        // 2. Check the ECS World
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Log("ERROR: DefaultGameObjectInjectionWorld is null!");
            return;
        }
        Log($"SUCCESS: Found ECS World: {world.Name}");
        
        Log("");
        
        // 3. Check for TransformFollowerAuthoring components
        var authoringComponents = FindObjectsOfType<TransformFollowerAuthoring>();
        Log($"Found {authoringComponents.Length} TransformFollowerAuthoring components:");
        foreach (var authoring in authoringComponents)
        {
            Log($"  - {authoring.gameObject.name}");
            Log($"    Target Mode: {authoring.targetMode}");
            Log($"    Target Name: '{authoring.targetName}'");
            Log($"    Target Tag: '{authoring.targetTag}'");
            Log($"    Offset: {authoring.offset}");
            Log($"    Follow Rotation: {authoring.followRotation}");
            Log($"    Smooth Time: {authoring.smoothTime}");
        }
        
        Log("");
        
        // 4. Query for entities with TransformFollowerSettings
        var entityManager = world.EntityManager;
        var query = entityManager.CreateEntityQuery(typeof(TransformFollowerSettings));
        int settingsCount = query.CalculateEntityCount();
        Log($"Found {settingsCount} entities with TransformFollowerSettings component");
        
        // 5. Query for entities with TransformReference
        var refQuery = entityManager.CreateEntityQuery(typeof(TransformReference));
        int refCount = refQuery.CalculateEntityCount();
        Log($"Found {refCount} entities with TransformReference component");
        
        // 6. Get detailed info about entities with both components
        var fullQuery = entityManager.CreateEntityQuery(
            typeof(TransformFollowerSettings),
            typeof(TransformReference));
        int fullCount = fullQuery.CalculateEntityCount();
        Log($"Found {fullCount} entities with BOTH components");
        
        if (fullCount > 0)
        {
            Log("\nDetailed entity info:");
            var entities = fullQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var settings = entityManager.GetComponentData<TransformFollowerSettings>(entity);
                var transformRef = entityManager.GetComponentObject<TransformReference>(entity);
                
                Log($"  Entity {i}:");
                Log($"    Offset: {settings.offset}");
                Log($"    Follow Rotation: {settings.followRotation}");
                Log($"    Smooth Time: {settings.smoothTime}");
                Log($"    Transform Reference: {(transformRef.target != null ? transformRef.target.name : "NULL")}");
                
                if (transformRef.target != null)
                {
                    Log($"    Target Position: {transformRef.target.position}");
                }
            }
            entities.Dispose();
        }
        
        Log("");
        
        // 7. Check if TransformFollowerSystem exists
        Log("Checking for TransformFollowerSystem...");
        var systems = world.Systems;
        bool foundSystem = false;
        foreach (var system in systems)
        {
            if (system.GetType().Name.Contains("TransformFollower"))
            {
                foundSystem = true;
                Log($"  Found system: {system.GetType().Name}");
                Log($"    Enabled: {system.Enabled}");
            }
        }
        if (!foundSystem)
        {
            Log("  WARNING: No TransformFollower system found!");
        }
        
        Log("\n=== End Debug Info ===");
        
        Debug.Log(debugOutput);
    }
    
    /// <summary>Appends <paramref name="message"/> to <see cref="debugOutput"/> with a trailing newline.</summary>
    void Log(string message)
    {
        debugOutput += message + "\n";
    }
    
    /// <summary>Returns the full scene hierarchy path of <paramref name="obj"/> (e.g. <c>"Root/Child/Leaf"</c>).</summary>
    string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
    
    /// <summary>Re-runs the debug check when the <c>D</c> key is pressed in play mode.</summary>
    void Update()
    {
        // Press D key to re-run debug
        if (Input.GetKeyDown(KeyCode.D))
        {
            DebugTransformFollowerSetup();
        }
    }
}

