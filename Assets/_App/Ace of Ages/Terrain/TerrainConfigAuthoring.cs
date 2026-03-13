using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for terrain system configuration.
/// Place this on a GameObject in your scene to configure the infinite terrain system.
/// </summary>
public class TerrainConfigAuthoring : MonoBehaviour
{
    [Header("Tile Settings")]
    [Tooltip("Size of each terrain tile in meters")]
    public float tileSize = 100f;
    
    [Tooltip("Distance from player that tiles remain active")]
    public float viewDistance = 500f;
    
    [Tooltip("Number of vertices per side of each tile (higher = more detailed)")]
    public int verticesPerSide = 32;
    
    [Header("Floating Origin")]
    [Tooltip("Enable floating origin to prevent floating-point precision errors")]
    public bool floatingOriginEnabled = true;
    
    [Tooltip("Distance from origin (0,0,0) that triggers a world shift")]
    public float shiftThreshold = 2000f;
    
    [Header("Procedural Noise Settings")]
    [Tooltip("Base frequency of the noise (higher = more variation)")]
    public float noiseFrequency = 0.01f;
    
    [Tooltip("Maximum height of terrain features")]
    public float noiseAmplitude = 20f;
    
    [Tooltip("Number of noise layers to combine")]
    [Range(1, 8)]
    public int noiseOctaves = 4;
    
    [Tooltip("Frequency multiplier for each octave")]
    public float noiseLacunarity = 2.0f;
    
    [Tooltip("Amplitude multiplier for each octave")]
    [Range(0f, 1f)]
    public float noisePersistence = 0.5f;
    
    [Header("Material")]
    [Tooltip("Material to use for terrain rendering (should use URP Lit shader)")]
    public Material terrainMaterial;

    public class Baker : Baker<TerrainConfigAuthoring>
    {
        public override void Bake(TerrainConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            // Create terrain tile config singleton
            AddComponent(entity, new TerrainTileConfig
            {
                tileSize = authoring.tileSize,
                viewDistance = authoring.viewDistance,
                verticesPerSide = authoring.verticesPerSide,
                noiseFrequency = authoring.noiseFrequency,
                noiseAmplitude = authoring.noiseAmplitude,
                noiseOctaves = authoring.noiseOctaves,
                noiseLacunarity = authoring.noiseLacunarity,
                noisePersistence = authoring.noisePersistence
            });
            
            // Create floating origin config singleton
            AddComponent(entity, new FloatingOriginConfig
            {
                enabled = authoring.floatingOriginEnabled,
                shiftThreshold = authoring.shiftThreshold
            });
            
            // Create world origin offset singleton (starts at zero)
            AddComponent(entity, new WorldOriginOffset
            {
                accumulatedOffset = double3.zero
            });
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Visualize view distance
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, viewDistance);
        
        // Visualize shift threshold
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, shiftThreshold);
        
        // Draw a sample tile
        Gizmos.color = Color.cyan;
        Vector3 tileCorner = transform.position;
        tileCorner.x = Mathf.Floor(tileCorner.x / tileSize) * tileSize;
        tileCorner.z = Mathf.Floor(tileCorner.z / tileSize) * tileSize;
        
        Vector3 size = new Vector3(tileSize, 0, tileSize);
        Gizmos.DrawWireCube(tileCorner + size * 0.5f, size);
    }

    private void OnValidate()
    {
        // Ensure valid values
        tileSize = Mathf.Max(1f, tileSize);
        viewDistance = Mathf.Max(tileSize, viewDistance);
        verticesPerSide = Mathf.Max(2, verticesPerSide);
        shiftThreshold = Mathf.Max(viewDistance * 2f, shiftThreshold);
        noiseFrequency = Mathf.Max(0.0001f, noiseFrequency);
        noiseAmplitude = Mathf.Max(0f, noiseAmplitude);
        noiseLacunarity = Mathf.Max(1f, noiseLacunarity);
    }
}

