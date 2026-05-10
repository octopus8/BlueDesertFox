using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace _App.Ace_of_Ages.Terrain
{
    /// <summary>
    /// Optimized system that creates physics colliders for terrain tiles.
    /// Target performance: less than 5ms during origin shifts (measured via profiler markers).
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TerrainColliderPreparationSystem))]
    public partial class TerrainPhysicsSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<TerrainTileConfig>();
        }

        protected override void OnUpdate()
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.enablePhysicsColliders)
            {
                return;
            }

            var preparedEntities = new NativeList<Entity>(64, Allocator.Temp);
            foreach (var (_, entity) in SystemAPI.Query<RefRO<PhysicsColliderPrepared>>()
                .WithAll<ColliderPreparedVertexElement, ColliderPreparedTriangleElement>()
                .WithEntityAccess())
            {
                preparedEntities.Add(entity);
            }

            if (preparedEntities.Length == 0)
            {
                preparedEntities.Dispose();
                return;
            }

            foreach (var entity in preparedEntities)
            {
                if (!EntityManager.Exists(entity))
                    continue;

                var vertexBuffer = EntityManager.GetBuffer<ColliderPreparedVertexElement>(entity);
                var triangleBuffer = EntityManager.GetBuffer<ColliderPreparedTriangleElement>(entity);

                if (vertexBuffer.Length == 0 || triangleBuffer.Length == 0)
                {
                    Debug.LogWarning($"[TerrainPhysics] Entity {entity.Index} has empty prepared buffers, skipping");
                    EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
                    continue;
                }

                var vertices = new NativeArray<float3>(vertexBuffer.Length, Allocator.Temp);
                var triangles = new NativeArray<int3>(triangleBuffer.Length, Allocator.Temp);

                for (int v = 0; v < vertexBuffer.Length; v++)
                {
                    vertices[v] = vertexBuffer[v].value;
                }

                for (int t = 0; t < triangleBuffer.Length; t++)
                {
                    triangles[t] = triangleBuffer[t].value;
                }

                try
                {
                    var collider = Unity.Physics.MeshCollider.Create(
                        vertices,
                        triangles,
                        CreateCollisionFilter(config),
                        Unity.Physics.Material.Default
                    );

                    EntityManager.AddComponentData(entity, new PhysicsCollider { Value = collider });

                    if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                    {
                        EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex());
                    }

                    EntityManager.AddComponent<PhysicsColliderValid>(entity);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TerrainPhysics] Failed to create collider for entity {entity.Index}: {e.Message}");
                }
                finally
                {
                    vertices.Dispose();
                    triangles.Dispose();
                }

                EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
            }

            preparedEntities.Dispose();
        }

        private CollisionFilter CreateCollisionFilter(TerrainTileConfig config)
        {
            uint layerMask = 1u << config.terrainPhysicsLayer;

            return new CollisionFilter
            {
                BelongsTo = layerMask,
                CollidesWith = ~0u,
                GroupIndex = 0
            };
        }
    }
}
