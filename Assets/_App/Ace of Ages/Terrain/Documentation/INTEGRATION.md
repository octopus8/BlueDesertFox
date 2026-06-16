# Integration Guide - Connecting Terrain to Game Systems

Guide for integrating the terrain system with other game systems, AI, gameplay mechanics, and project architecture.

## Integration Categories

- [Player/Character Integration](#player-character-integration)
- [AI Navigation](#ai-navigation)
- [Gameplay Mechanics](#gameplay-mechanics)
- [UI Integration](#ui-integration)
- [Save/Load System](#save-load-system)
- [Multiplayer](#multiplayer-considerations)

---

## Player/Character Integration

### XR Player Integration

The terrain system tracks whichever GameObject you configure via `TerrainConfigAuthoring`. For XR rigs, use `FindByName` or `FindByTag`:

```csharp
// TerrainConfigAuthoring setup — example for an XR Origin rig:
Player Search Mode: FindByName
Player Name: "XR Origin Hands (XR Rig)"

// Or tag-based:
Player Search Mode: FindByTag
Player Tag: "Player"
```

**No additional code needed** - works out of the box!

---

### Custom Character Controller

For non-VR character controllers:

**Option 1: Tag-Based**
```csharp
// On your player prefab
Tag: "Player"

// TerrainConfigAuthoring:
Player Search Mode: FindByTag
Player Tag: "Player"
```

**Option 2: Name-Based**
```csharp
// TerrainConfigAuthoring:
Player Search Mode: FindByName
Player Name: "ThirdPersonController"
```

---

### Multiple Characters

Track multiple characters by creating multiple terrain instances:

```csharp
public class MultiPlayerTerrain : MonoBehaviour
{
    public GameObject player1;
    public GameObject player2;
    
    void Start()
    {
        // Create two terrain configs in separate SubScenes
        // Each tracks different player
        CreateTerrainForPlayer(player1, "TerrainSubScene1");
        CreateTerrainForPlayer(player2, "TerrainSubScene2");
    }
}
```

**Note**: Expensive - each terrain is independent system. Consider tracking a "center point" GameObject instead.

---

## AI Navigation

### NavMesh on Terrain

Unity NavMesh doesn't work directly with ECS terrain. Two approaches:

#### Approach 1: Physics-Based AI

Use raycasts for pathfinding:

```csharp
public class TerrainAI : MonoBehaviour
{
    public Vector3 GetGroundPosition(Vector3 position)
    {
        var ray = new Ray(position + Vector3.up * 100f, Vector3.down);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            return hit.point;  // Position on terrain
        }
        
        return position;  // Fallback
    }
    
    public bool CanMoveTo(Vector3 from, Vector3 to)
    {
        // Check line of sight on terrain
        return !Physics.Linecast(from, to);
    }
}
```

#### Approach 2: Grid-Based Pathfinding

Build navigation grid from terrain:

```csharp
public class TerrainNavGrid
{
    private float[,] _heightMap;
    private bool[,] _walkable;
    
    public void BuildFromTerrain(int resolution)
    {
        _heightMap = new float[resolution, resolution];
        _walkable = new bool[resolution, resolution];
        
        // Sample terrain heights
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector3 worldPos = new Vector3(x * 10f, 0, z * 10f);
                _heightMap[x, z] = SampleTerrainHeight(worldPos);
                
                // Check walkability (slope check)
                float slope = CalculateSlope(x, z);
                _walkable[x, z] = slope < 0.7f;  // Not too steep
            }
        }
    }
    
    float SampleTerrainHeight(Vector3 worldPos)
    {
        var ray = new Ray(worldPos + Vector3.up * 100f, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            return hit.point.y;
        return 0f;
    }
}
```

---

### Spawn Objects With Scrolling Terrain (ECS)

For spawned objects (obstacles, pickups, decorations) that need to move with scrolling terrain, use `TerrainAnchorTag`:

**Step 1: Create Prefab with TerrainAnchorTagAuthoring**

```csharp
// Create SubScene with your obstacle prefab
// Add TerrainAnchorTagAuthoring component to it
TerrainAnchorTagAuthoring
├─ Use Custom Base Position: false  // Uses spawn position
```

**Step 2: Bake Prefab to Entity**

Unity's baking system will convert the prefab to an entity with `TerrainAnchorTag` component.

**Step 3: Spawn via Instantiate**

```csharp
public partial class ObstacleSpawnerSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        
        // Get obstacle prefab entity (set in authoring)
        var spawnerConfig = SystemAPI.GetSingleton<ObstacleSpawnerConfig>();
        
        // Spawn obstacle at specific position
        Entity obstacle = EntityManager.Instantiate(spawnerConfig.obstaclePrefab);
        
        // Set initial transform
        EntityManager.SetComponentData(obstacle, new LocalTransform
        {
            Position = new float3(100, 10, 50),
            Rotation = quaternion.identity,
            Scale = 1f
        });
        
        // Update TerrainAnchorTag.basePosition to current world position
        // This ensures obstacle moves with terrain from this point forward
        EntityManager.SetComponentData(obstacle, new TerrainAnchorTag
        {
            basePosition = new float3(100, 10, 50)
        });
        
        // TerrainAnchorSystem will automatically update position each frame:
        // obstacle.Position = basePosition - scrollOffset.accumulatedOffset
    }
}
```

**Step 4: System Handles Movement Automatically**

`TerrainAnchorSystem` runs every frame and updates all entities with `TerrainAnchorTag`:
- No manual update code needed
- Parallel Burst-compiled job (optimal performance)
- Works seamlessly with scrolling terrain

**Performance**: 
- 1000 anchored obstacles: ~0.4-0.6ms per frame (Quest 3)
- Zero GC allocations
- Scales efficiently with entity count

**When to Use TerrainAnchor**:
- ✅ Spawned obstacles/decorations (rocks, bushes)
- ✅ Collectible items (coins, powerups)
- ✅ Environmental hazards
- ✅ Any non-tile entity that needs to scroll

**When NOT to Use**:
- ❌ **Trees** - Use `TreeTileOwnership` + `TreePositionUpdateSystem`
- ❌ **Player/Camera** - Should remain stationary
- ❌ **UI Elements** - Use screen space

---

## Gameplay Mechanics

### Spawn Objects on Terrain

Spawn gameplay objects at terrain height:

```csharp
public class TerrainObjectSpawner : MonoBehaviour
{
    public GameObject prefab;
    
    public void SpawnOnTerrain(Vector3 xzPosition)
    {
        // Raycast down to find terrain height
        var ray = new Ray(new Vector3(xzPosition.x, 100f, xzPosition.z), Vector3.down);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            // Spawn at terrain surface
            Instantiate(prefab, hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal));
        }
    }
}
```

---

### Terrain-Based Gameplay Events

Trigger events at specific tile positions:

```csharp
public class TerrainEventTrigger : MonoBehaviour
{
    public float eventRadius = 50f;
    
    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        foreach (var (tile, transform) in 
            SystemAPI.Query<RefRO<TerrainTile>, RefRO<LocalTransform>>())
        {
            float3 tileCenter = transform.ValueRO.Position + new float3(50, 0, 50);
            float dist = math.distance(tileCenter, (float3)transform.position);
            
            if (dist < eventRadius)
            {
                OnTileNearby(tile.ValueRO.gridCoordinate);
            }
        }
    }
    
    void OnTileNearby(int2 gridCoord)
    {
        // Trigger gameplay event
        // Example: Spawn enemies, show tutorial, etc.
    }
}
```

---

### Resource Gathering

Place resources at terrain features:

```csharp
public class ResourcePlacer : MonoBehaviour
{
    void PlaceResourcesOnTile(Entity tileEntity)
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var vertices = em.GetBuffer<VertexElement>(tileEntity);
        var tile = em.GetComponentData<TerrainTile>(tileEntity);
        
        // Find high peaks (resource spawn points)
        foreach (var vertex in vertices)
        {
            if (vertex.value.y > 30f)  // High altitude
            {
                float3 worldPos = tile.gridCoordinate * 100f + vertex.value;
                SpawnResource(worldPos);
            }
        }
    }
}
```

---

## UI Integration

### Display Scroll Distance

```csharp
using Unity.Entities;
using Unity.Mathematics;
using TMPro;
using UnityEngine;

public class ScrollDistanceUI : MonoBehaviour
{
    public TextMeshProUGUI distanceText;
    
    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollOffset));
        
        if (query.CalculateEntityCount() > 0)
        {
            var offset = em.GetComponentData<ScrollOffset>(query.GetSingletonEntity());
            float distance = math.length(offset.accumulatedOffset);
            
            distanceText.text = $"Distance: {distance:F0}m";
        }
        
        query.Dispose();
    }
}
```

---

### Terrain Settings UI

Runtime configuration panel:

```csharp
public class TerrainSettingsUI : MonoBehaviour
{
    public Slider viewDistanceSlider;
    public Toggle scrollToggle;
    public Slider scrollSpeedSlider;
    
    void Start()
    {
        viewDistanceSlider.onValueChanged.AddListener(OnViewDistanceChanged);
        scrollToggle.onValueChanged.AddListener(OnScrollToggled);
        scrollSpeedSlider.onValueChanged.AddListener(OnScrollSpeedChanged);
    }
    
    void OnViewDistanceChanged(float value)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTileConfig));
        var entity = query.GetSingletonEntity();
        var config = em.GetComponentData<TerrainTileConfig>(entity);
        
        config.viewDistance = value;
        em.SetComponentData(entity, config);
        query.Dispose();
    }
    
    void OnScrollToggled(bool enabled)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollConfig));
        var entity = query.GetSingletonEntity();
        var config = em.GetComponentData<ScrollConfig>(entity);
        
        config.enabled = enabled;
        em.SetComponentData(entity, config);
        query.Dispose();
    }
}
```

---

## Save/Load System

### Save Terrain Configuration

```csharp
[System.Serializable]
public class TerrainSaveData
{
    public float tileSize;
    public float viewDistance;
    public int verticesPerSide;
    public bool scrollEnabled;
    public float scrollSpeed;
    // ... other config fields
}

public class TerrainSaveSystem : MonoBehaviour
{
    public void SaveConfiguration(string filePath)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        var configQuery = em.CreateEntityQuery(typeof(TerrainTileConfig));
        var config = em.GetComponentData<TerrainTileConfig>(configQuery.GetSingletonEntity());
        configQuery.Dispose();
        
        var scrollQuery = em.CreateEntityQuery(typeof(ScrollConfig));
        var scroll = em.GetComponentData<ScrollConfig>(scrollQuery.GetSingletonEntity());
        scrollQuery.Dispose();
        
        var saveData = new TerrainSaveData
        {
            tileSize = config.tileSize,
            viewDistance = config.viewDistance,
            verticesPerSide = config.verticesPerSide,
            scrollEnabled = scroll.enabled,
            scrollSpeed = scroll.scrollSpeed
        };
        
        string json = JsonUtility.ToJson(saveData, true);
        System.IO.File.WriteAllText(filePath, json);
    }
    
    public void LoadConfiguration(string filePath)
    {
        string json = System.IO.File.ReadAllText(filePath);
        var saveData = JsonUtility.FromJson<TerrainSaveData>(json);
        
        // Apply to terrain config...
    }
}
```

---

### Save Scroll Progress

```csharp
public void SaveScrollProgress()
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    var query = em.CreateEntityQuery(typeof(ScrollOffset));
    var offset = em.GetComponentData<ScrollOffset>(query.GetSingletonEntity());
    query.Dispose();
    
    PlayerPrefs.SetFloat("ScrollDistance", math.length(offset.accumulatedOffset));
    PlayerPrefs.SetFloat("ScrollX", offset.accumulatedOffset.x);
    PlayerPrefs.SetFloat("ScrollY", offset.accumulatedOffset.y);
    PlayerPrefs.SetFloat("ScrollZ", offset.accumulatedOffset.z);
    PlayerPrefs.Save();
}

public void LoadScrollProgress()
{
    float3 loadedOffset = new float3(
        PlayerPrefs.GetFloat("ScrollX", 0),
        PlayerPrefs.GetFloat("ScrollY", 0),
        PlayerPrefs.GetFloat("ScrollZ", 0)
    );
    
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    var query = em.CreateEntityQuery(typeof(ScrollOffset));
    var entity = query.GetSingletonEntity();
    
    em.SetComponentData(entity, new ScrollOffset { accumulatedOffset = loadedOffset });
    query.Dispose();
}
```

---

## Multiplayer Considerations

### Synchronizing Terrain

**Challenge**: Each client generates terrain procedurally  
**Solution**: Use **deterministic noise** with shared seed

```csharp
public struct TerrainTileConfig : IComponentData
{
    // Add field:
    public uint noiseSeed;  // Shared across clients
}

// In noise generation:
var random = new Unity.Mathematics.Random(config.noiseSeed);
float2 offset = random.NextFloat2() * 10000f;
float sample = noise.cnoise((worldPosition + offset) * frequency);
```

**Result**: All clients generate identical terrain from same seed.

---

### Network Player Position

Synchronize which player to track:

```csharp
public class NetworkTerrainSync : MonoBehaviour
{
    public void OnLocalPlayerSpawned(GameObject localPlayer)
    {
        // Update PlayerTransformReference to track local player
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
        var entity = query.GetSingletonEntity();
        var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
        
        playerRef.playerTransform = localPlayer.transform;
        query.Dispose();
        
        Debug.Log($"Terrain now tracking local player: {localPlayer.name}");
    }
}
```

---

### Synchronized Scrolling

Share scroll offset across network:

```csharp
[System.Serializable]
public struct NetworkScrollData
{
    public bool enabled;
    public float speed;
    public float3 offset;
}

public class NetworkScrollSync : MonoBehaviour
{
    // Send to server/clients every frame
    public NetworkScrollData GetScrollData()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        var configQuery = em.CreateEntityQuery(typeof(ScrollConfig));
        var config = em.GetComponentData<ScrollConfig>(configQuery.GetSingletonEntity());
        configQuery.Dispose();
        
        var offsetQuery = em.CreateEntityQuery(typeof(ScrollOffset));
        var offset = em.GetComponentData<ScrollOffset>(offsetQuery.GetSingletonEntity());
        offsetQuery.Dispose();
        
        return new NetworkScrollData
        {
            enabled = config.enabled,
            speed = config.scrollSpeed,
            offset = offset.accumulatedOffset
        };
    }
    
    // Receive from server/clients
    public void ApplyScrollData(NetworkScrollData data)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        // Apply config
        var configQuery = em.CreateEntityQuery(typeof(ScrollConfig));
        em.SetComponentData(configQuery.GetSingletonEntity(), new ScrollConfig
        {
            enabled = data.enabled,
            scrollSpeed = data.speed
        });
        configQuery.Dispose();
        
        // Apply offset
        var offsetQuery = em.CreateEntityQuery(typeof(ScrollOffset));
        em.SetComponentData(offsetQuery.GetSingletonEntity(), new ScrollOffset
        {
            accumulatedOffset = data.offset
        });
        offsetQuery.Dispose();
    }
}
```

---

## AI Navigation

### Simple AI Pathfinding

Basic grid-based pathfinding on terrain:

```csharp
public class TerrainPathfinder : MonoBehaviour
{
    public Vector3 FindPath(Vector3 start, Vector3 goal)
    {
        // Sample points along straight line
        Vector3 direction = (goal - start).normalized;
        float distance = Vector3.Distance(start, goal);
        
        for (float d = 0; d < distance; d += 5f)
        {
            Vector3 checkPoint = start + direction * d;
            
            // Check if this point is walkable
            if (!IsWalkable(checkPoint))
            {
                // Find alternate path (simplified A*)
                return FindAlternatePath(start, goal);
            }
        }
        
        return goal;  // Direct path clear
    }
    
    bool IsWalkable(Vector3 position)
    {
        // Raycast to terrain
        var ray = new Ray(position + Vector3.up * 50f, Vector3.down);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            // Check slope
            float slope = 1f - hit.normal.y;
            return slope < 0.5f;  // Walkable if not too steep
        }
        
        return false;
    }
}
```

---

### AI Formation Movement

Keep AI on terrain surface during formation movement:

```csharp
public class TerrainFormationAI : MonoBehaviour
{
    public void MoveFormation(Vector3 targetPosition, List<GameObject> units)
    {
        Vector3 leaderTarget = ClampToTerrain(targetPosition);
        
        foreach (var unit in units)
        {
            Vector3 formationOffset = CalculateFormationOffset(unit);
            Vector3 unitTarget = ClampToTerrain(leaderTarget + formationOffset);
            
            unit.transform.position = Vector3.MoveTowards(
                unit.transform.position,
                unitTarget,
                Time.deltaTime * 5f
            );
        }
    }
    
    Vector3 ClampToTerrain(Vector3 position)
    {
        var ray = new Ray(new Vector3(position.x, 100f, position.z), Vector3.down);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            return hit.point + Vector3.up * 0.5f;  // Slightly above surface
        }
        
        return position;
    }
}
```

---

## Gameplay Mechanics

### Endless Runner Integration

Full endless runner with obstacles:

```csharp
public class EndlessRunnerController : MonoBehaviour
{
    public GameObject obstaclePrefab;
    public float obstacleSpawnInterval = 30f;
    
    private float _lastObstacleDistance = 0f;
    
    void Start()
    {
        EnableScrolling(10f);
    }
    
    void Update()
    {
        float currentDistance = GetScrollDistance();
        
        // Spawn obstacle every 30m
        if (currentDistance >= _lastObstacleDistance + obstacleSpawnInterval)
        {
            SpawnObstacle();
            _lastObstacleDistance = currentDistance;
        }
        
        // Increase difficulty over time
        float speed = 10f + Mathf.Floor(currentDistance / 200f) * 2f;
        SetScrollSpeed(Mathf.Min(speed, 25f));
    }
    
    void SpawnObstacle()
    {
        // Spawn ahead of player
        Vector3 spawnPos = transform.position + transform.forward * 100f;
        spawnPos = ClampToTerrain(spawnPos);
        
        Instantiate(obstaclePrefab, spawnPos, Quaternion.identity);
    }
}
```

---

### Racing Game Integration

Terrain as racing track:

```csharp
public class RacingTrackGenerator : MonoBehaviour
{
    public AnimationCurve trackPath;
    
    void GenerateTrack()
    {
        // Flatten terrain along track path
        // Use terrain modification system (see Extensions)
        
        for (float t = 0; t < 1f; t += 0.01f)
        {
            Vector3 pathPoint = EvaluateTrackPath(t);
            FlattenTerrainAt(pathPoint, radius: 10f, targetHeight: 0f);
        }
    }
}
```

---

## Scene Management Integration

### Loading Terrain with Scene

**In SceneStartup.cs** (if using project's scene management):

```csharp
public class SceneStartup : MonoBehaviour
{
    public SubScene terrainSubScene;
    
    async UniTask Start()
    {
        // Set tracking origin
        DeviceTracking.Instance.UpdateImmediate();
        
        // Fade out camera
        await CameraFader.Instance.FadeOut();
        
        // Load terrain SubScene
        SubSceneLoader.Instance.LoadScene(terrainSubScene.SceneGUID);
        
        // Fade in
        await CameraFader.Instance.FadeIn();
        
        // Terrain should spawn around player automatically
    }
}
```

---

### Unloading Terrain

```csharp
public class TerrainUnloader : MonoBehaviour
{
    public void UnloadTerrain()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        // Destroy all terrain tiles
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        em.DestroyEntity(query);
        query.Dispose();
        
        Debug.Log("Terrain unloaded");
    }
}
```

---

## Performance Integration

### Adaptive Quality System

Adjust terrain quality based on frame rate:

```csharp
public class AdaptiveTerrainQuality : MonoBehaviour
{
    private float _targetFrameTime = 0.011f;  // 90fps
    private float[] _frameTimeSamples = new float[60];
    private int _sampleIndex = 0;
    
    void Update()
    {
        // Track frame times
        _frameTimeSamples[_sampleIndex] = Time.deltaTime;
        _sampleIndex = (_sampleIndex + 1) % _frameTimeSamples.Length;
        
        // Every second, check performance
        if (Time.frameCount % 60 == 0)
        {
            float avgFrameTime = _frameTimeSamples.Average();
            
            if (avgFrameTime > _targetFrameTime * 1.3f)
            {
                // Performance bad - reduce quality
                ReduceTerrainQuality();
            }
            else if (avgFrameTime < _targetFrameTime * 0.7f)
            {
                // Performance good - increase quality
                IncreaseTerrainQuality();
            }
        }
    }
    
    void ReduceTerrainQuality()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTileConfig));
        var entity = query.GetSingletonEntity();
        var config = em.GetComponentData<TerrainTileConfig>(entity);
        
        // Reduce vertices
        config.verticesPerSide = Mathf.Max(8, config.verticesPerSide / 2);
        
        // Reduce view distance
        config.viewDistance = Mathf.Max(200f, config.viewDistance * 0.8f);
        
        em.SetComponentData(entity, config);
        query.Dispose();
        
        Debug.Log($"Reduced quality: vertices={config.verticesPerSide}, view={config.viewDistance}m");
    }
}
```

---

## Analytics Integration

### Track Terrain Performance Metrics

```csharp
public class TerrainAnalytics : MonoBehaviour
{
    void Update()
    {
        if (Time.frameCount % 300 == 0)  // Every 5 seconds
        {
            var stats = CollectTerrainStats();
            SendToAnalytics(stats);
        }
    }
    
    TerrainStats CollectTerrainStats()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
        var meshQuery = em.CreateEntityQuery(typeof(TerrainTile), typeof(MeshReference));
        var physicsQuery = em.CreateEntityQuery(typeof(TerrainTile), typeof(Unity.Physics.PhysicsCollider));
        
        var stats = new TerrainStats
        {
            activeTiles = tileQuery.CalculateEntityCount(),
            renderedTiles = meshQuery.CalculateEntityCount(),
            physicsTiles = physicsQuery.CalculateEntityCount(),
            frameTime = Time.deltaTime * 1000f,
            fps = 1f / Time.deltaTime
        };
        
        tileQuery.Dispose();
        meshQuery.Dispose();
        physicsQuery.Dispose();
        
        return stats;
    }
}
```

---

## Input System Integration

### Terrain-Aware Input

Raycast to terrain for input handling:

```csharp
public class TerrainInputHandler : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Check if hit terrain
                // (Terrain entities don't have GameObject, check layer or bounds)
                OnTerrainClicked(hit.point, hit.normal);
            }
        }
    }
    
    void OnTerrainClicked(Vector3 position, Vector3 normal)
    {
        Debug.Log($"Clicked terrain at {position}");
        // Spawn object, modify terrain, etc.
    }
}
```

---

## Audio Integration

### Footstep Sounds Based on Terrain

```csharp
public class TerrainFootstepAudio : MonoBehaviour
{
    public AudioClip grassFootstep;
    public AudioClip rockFootstep;
    public AudioClip snowFootstep;
    
    private AudioSource _audioSource;
    
    public void PlayFootstep(Vector3 position)
    {
        // Raycast to get terrain surface
        var ray = new Ray(position + Vector3.up * 1f, Vector3.down);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 2f))
        {
            float height = hit.point.y;
            
            // Choose sound based on height (biome)
            AudioClip clip;
            if (height < 10f)
                clip = grassFootstep;
            else if (height < 40f)
                clip = rockFootstep;
            else
                clip = snowFootstep;
            
            _audioSource.PlayOneShot(clip);
        }
    }
}
```

---

## Particle System Integration

### Terrain-Aligned Particle Effects

```csharp
public class TerrainParticleEffect : MonoBehaviour
{
    public ParticleSystem dustEffect;
    
    public void PlayEffectOnTerrain(Vector3 position)
    {
        var ray = new Ray(position + Vector3.up * 50f, Vector3.down);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            // Spawn at terrain surface with correct normal
            var effect = Instantiate(dustEffect, hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal));
            effect.Play();
        }
    }
}
```

---

## Weather System Integration

### Dynamic Weather Effects on Terrain

```csharp
public class TerrainWeatherSystem : MonoBehaviour
{
    public enum Weather { Clear, Rain, Snow }
    public Weather currentWeather = Weather.Clear;
    
    void Update()
    {
        var material = GetTerrainMaterial();
        if (material == null) return;
        
        switch (currentWeather)
        {
            case Weather.Rain:
                material.SetFloat("_Wetness", 0.8f);
                material.SetFloat("_Smoothness", 0.6f);
                break;
            case Weather.Snow:
                material.SetFloat("_SnowCoverage", 0.9f);
                material.SetColor("_BaseColor", Color.white);
                break;
            case Weather.Clear:
                material.SetFloat("_Wetness", 0f);
                material.SetFloat("_SnowCoverage", 0f);
                break;
        }
    }
    
    Material GetTerrainMaterial()
    {
        return Resources.Load<Material>("TerrainMaterial");
    }
}
```

---

## Time-of-Day Integration

### Dynamic Terrain Lighting

```csharp
public class TerrainDayNightCycle : MonoBehaviour
{
    public Light directionalLight;
    public float dayDuration = 120f;  // 2 minutes per day
    
    void Update()
    {
        float timeOfDay = (Time.time % dayDuration) / dayDuration;  // 0-1
        
        // Rotate sun
        float angle = timeOfDay * 360f - 90f;  // -90 to 270
        directionalLight.transform.rotation = Quaternion.Euler(angle, 0, 0);
        
        // Adjust terrain material
        var material = GetTerrainMaterial();
        if (material != null)
        {
            // Darker at night
            float brightness = Mathf.Lerp(0.3f, 1f, Mathf.Abs(Mathf.Cos(timeOfDay * Mathf.PI)));
            material.SetFloat("_Brightness", brightness);
        }
    }
}
```

---

## Best Practices

### Integration Checklist

When integrating terrain:

```
✅ Test with terrain active (don't integrate in isolation)
✅ Profile combined systems (check total frame time)
✅ Handle terrain loading/unloading
✅ Respect terrain coordinate system (don't assume origin)
✅ Use Physics.Raycast for terrain queries (don't access ECS directly)
✅ Cache terrain data when possible (don't query every frame)
```

### Anti-Patterns

❌ **Don't** access ECS components from gameplay code (use Physics)  
❌ **Don't** modify terrain config every frame (expensive)  
❌ **Don't** assume terrain is always loaded (check before querying)  
❌ **Don't** store tile Entity references (tiles destroy/recreate)  
❌ **Don't** bypass frame budgets (causes spikes)

---

## Example: Complete Game Integration

### Endless Runner with Scoring

```csharp
public class EndlessRunnerGame : MonoBehaviour
{
    [Header("Terrain")]
    public float scrollSpeed = 10f;
    
    [Header("Gameplay")]
    public GameObject obstaclePrefab;
    public float obstacleInterval = 40f;
    
    [Header("UI")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI distanceText;
    
    private float _score = 0f;
    private float _lastObstacleDistance = 0f;
    
    void Start()
    {
        InitializeTerrain();
    }
    
    void InitializeTerrain()
    {
        // Enable scrolling
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollConfig));
        var entity = query.GetSingletonEntity();
        
        em.SetComponentData(entity, new ScrollConfig 
        { 
            enabled = true, 
            scrollSpeed = scrollSpeed 
        });
        
        query.Dispose();
    }
    
    void Update()
    {
        UpdateGameplay();
        UpdateUI();
    }
    
    void UpdateGameplay()
    {
        float distance = GetScrollDistance();
        _score = distance;  // Score = distance traveled
        
        // Spawn obstacles
        if (distance >= _lastObstacleDistance + obstacleInterval)
        {
            SpawnObstacleOnTerrain();
            _lastObstacleDistance = distance;
        }
        
        // Increase difficulty
        float newSpeed = 10f + Mathf.Floor(distance / 200f) * 1.5f;
        SetScrollSpeed(Mathf.Min(newSpeed, 25f));
    }
    
    void UpdateUI()
    {
        scoreText.text = $"Score: {_score:F0}";
        distanceText.text = $"{_score:F0}m";
    }
    
    void SpawnObstacleOnTerrain()
    {
        Vector3 spawnPos = transform.position + transform.forward * 80f;
        
        // Clamp to terrain
        var ray = new Ray(spawnPos + Vector3.up * 50f, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            Instantiate(obstaclePrefab, hit.point, Quaternion.identity);
        }
    }
    
    float GetScrollDistance()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollOffset));
        var offset = em.GetComponentData<ScrollOffset>(query.GetSingletonEntity());
        query.Dispose();
        return math.length(offset.accumulatedOffset);
    }
    
    void SetScrollSpeed(float speed)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollConfig));
        var entity = query.GetSingletonEntity();
        var config = em.GetComponentData<ScrollConfig>(entity);
        config.scrollSpeed = speed;
        em.SetComponentData(entity, config);
        query.Dispose();
    }
}
```

---

## Related Documentation

- **[API Reference](API_REFERENCE.md)** - Component APIs for integration
- **[Technical Details](TECHNICAL_DETAILS.md)** - Implementation details
- **[Performance Optimization](PERFORMANCE.md)** - Optimizing integrated systems
- **[Auto-Scrolling Guide](AUTO_SCROLLING.md)** - Scrolling feature details

---

**Back to**: [Documentation Hub](README.md)

