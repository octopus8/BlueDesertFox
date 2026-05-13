using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Re-fires the authored initial event on each pooled dirt-explosion <see cref="VisualEffect"/>
/// the first frame it is activated, so the burst plays every time a pool entity is reused.
/// </summary>
/// <remarks>
/// Reusing a pooled <see cref="VisualEffect"/> companion does NOT re-fire its
/// <see cref="VisualEffect.initialEventName"/> burst: the auto-OnPlay only happens once,
/// during the companion GameObject's first <c>OnEnable</c>. Neither <see cref="VisualEffect.Reinit"/>
/// nor <see cref="VisualEffect.Play"/> alone will resend the event - only an explicit
/// <see cref="VisualEffect.SendEvent(string)"/> will. We do all three for robustness.
///
/// Runs in <see cref="PresentationSystemGroup"/> with <c>OrderLast = true</c> so the
/// per-entity companion is guaranteed to exist and have been transform-synced by Entities
/// Graphics' companion update systems before we reposition and replay it.
///
/// Cannot be <c>ISystem</c>/Burst-compiled because it touches managed
/// <see cref="UnityEngine.VFX.VisualEffect"/> instances.
/// </remarks>
[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
public partial class DirtExplosionPlaySystem : SystemBase
{
    private string _cachedInitialEvent;

    protected override void OnCreate()
    {
        RequireForUpdate<DirtExplosion>();
    }

    protected override void OnUpdate()
    {
        var em = EntityManager;

        foreach (var (data, ltw, entity) in SystemAPI.Query<RefRW<DirtExplosionData>, RefRO<LocalToWorld>>()
                     .WithAll<DirtExplosion>()
                     .WithEntityAccess())
        {
            ref var d = ref data.ValueRW;

            if (!d.active || d.triggered)
                continue;

            if (!em.HasComponent<VisualEffect>(entity))
                continue;

            var vfx = em.GetComponentObject<VisualEffect>(entity);
            if (vfx == null)
                continue;

            // initialEventName is authored on the prefab and constant at runtime; cache once.
            if (_cachedInitialEvent == null)
            {
                var authored = vfx.initialEventName;
                _cachedInitialEvent = string.IsNullOrEmpty(authored) ? "OnPlay" : authored;
            }

            // Force the companion transform to match the entity right now, in case the
            // standard companion-transform sync has not run yet this frame for newly
            // activated pool entries.
            vfx.transform.SetPositionAndRotation(ltw.ValueRO.Position, ltw.ValueRO.Rotation);

            vfx.pause = false;
            vfx.Reinit();
            vfx.Play();
            vfx.SendEvent(_cachedInitialEvent);

            d.triggered = true;
        }
    }
}
