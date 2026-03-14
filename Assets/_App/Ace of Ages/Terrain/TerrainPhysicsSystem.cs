using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Unity.Collections;

/// <summary>
/// System that creates physics colliders for terrain tiles.
/// Mesh colliders are generated from the terrain mesh data.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial class TerrainPhysicsSystem : SystemBase
{
    private EntityQuery _tilesNeedingCollidersQuery;
    private NativeHashSet<Entity> _createdColliders;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
        
        // Query for tiles that have mesh data but no PhysicsCollider yet
        _tilesNeedingCollidersQuery = GetEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.Exclude<PhysicsCollider>()
        );
        
        // Track entities whose colliders we created (not deserialized from SubScene)
        _createdColliders = new NativeHashSet<Entity>(128, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        // Process tiles that need collider creation
        var entities = _tilesNeedingCollidersQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        foreach (var entity in entities)
        {
            var tile = EntityManager.GetComponentData<TerrainTile>(entity);
            
            if (tile.meshGenerated)
            {
                var vertices = EntityManager.GetBuffer<VertexElement>(entity);
                var indices = EntityManager.GetBuffer<IndexElement>(entity);
                
                if (vertices.Length > 0 && indices.Length > 0)
                {
                    CreatePhysicsCollider(entity, vertices, indices);
                }
            }
        }
        
        entities.Dispose();
    }

    /// <summary>
    /// Creates a mesh collider for the terrain tile using Unity.Physics.
    /// </summary>
    private void CreatePhysicsCollider(
        Entity entity,
        DynamicBuffer<VertexElement> vertexBuffer,
        DynamicBuffer<IndexElement> indexBuffer)
    {
        try
        {
            // Convert buffers to Unity.Physics format
            var vertices = new Unity.Collections.NativeArray<float3>(vertexBuffer.Length, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < vertexBuffer.Length; i++)
            {
                vertices[i] = vertexBuffer[i].value;
            }
            
            var triangles = new Unity.Collections.NativeArray<int3>(indexBuffer.Length / 3, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = new int3(
                    indexBuffer[i * 3].value,
                    indexBuffer[i * 3 + 1].value,
                    indexBuffer[i * 3 + 2].value
                );
            }
            
            // Create mesh collider
            var collider = Unity.Physics.MeshCollider.Create(
                vertices,
                triangles,
                new CollisionFilter
                {
                    BelongsTo = 1u << 0, // Default layer
                    CollidesWith = ~0u, // Collide with everything
                    GroupIndex = 0
                },
                Unity.Physics.Material.Default
            );
            
            // Add PhysicsCollider component
            EntityManager.AddComponentData(entity, new PhysicsCollider { Value = collider });
            
            // Track that we created this collider (so we know to dispose it later)
            _createdColliders.Add(entity);
            
            // Add PhysicsWorldIndex if not present (for multi-world physics)
            if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
            {
                EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex());
            }
            
            vertices.Dispose();
            triangles.Dispose();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TerrainPhysics] Failed to create collider for entity {entity.Index}: {e.Message}");
        }
    }

    protected override void OnDestroy()
    {
        // Clean up only colliders that we created in code (not deserialized from SubScene)
        // Deserialized blob assets are automatically released when the scene is unloaded
        foreach (var entity in _createdColliders)
        {
            if (EntityManager.Exists(entity) && EntityManager.HasComponent<PhysicsCollider>(entity))
            {
                var collider = EntityManager.GetComponentData<PhysicsCollider>(entity);
                if (collider.IsValid)
                {
                    collider.Value.Dispose();
                }
            }
        }
        
        _createdColliders.Dispose();
    }
}




