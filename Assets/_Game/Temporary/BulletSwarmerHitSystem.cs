using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// For each alive bullet, checks distance against all alive swarmer positions.
/// On hit:
///   • AoE (ExplosionRadius &gt; 0): splash all swarmers within radius.
///   • Direct damage to the hit swarmer.
///   • Decrement penetration; mark bullet dead when exhausted.
///
/// Phase C will replace this O(n·m) scan with Psyshock FindPairs.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletSwarmerHitSystem : SystemBase
{
    private EntityQuery m_swarmerQuery;

    protected override void OnCreate()
    {
        m_swarmerQuery = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerRadius>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());
    }

    protected override void OnUpdate()
    {
        // Snapshot swarmer state for this tick.
        var swarmerEntities  = m_swarmerQuery.ToEntityArray(Allocator.Temp);
        var swarmerPositions = m_swarmerQuery.ToComponentDataArray<SwarmerPosition>(Allocator.Temp);
        var swarmerRadii     = m_swarmerQuery.ToComponentDataArray<SwarmerRadius>(Allocator.Temp);

        foreach (var (pos, data, companionRef, dead) in
            SystemAPI.Query<
                RefRO<BulletPosition>,
                RefRW<BulletData>,
                BulletCompanionRef,
                EnabledRefRW<BulletDeadTag>>()
            .WithDisabled<BulletDeadTag>())
        {
            float3 bulletPos = pos.ValueRO.Value;
            bool exploded    = false;

            for (int si = 0; si < swarmerEntities.Length; si++)
            {
                float dist = math.distance(bulletPos, swarmerPositions[si].Value);
                float combinedRadius = data.ValueRO.ColliderRadius + swarmerRadii[si].Value;

                if (dist > combinedRadius) continue;

                var swarmerRef = EntityManager.GetComponentObject<SwarmerCompanionRef>(swarmerEntities[si]);
                if (swarmerRef == null || swarmerRef.MB == null) continue;

                // AoE splash (only once per bullet death).
                if (data.ValueRO.ExplosionRadius > 0f && !exploded)
                {
                    exploded = true;
                    float sqrExplosion = data.ValueRO.ExplosionRadius * data.ValueRO.ExplosionRadius;
                    for (int sj = 0; sj < swarmerEntities.Length; sj++)
                    {
                        float sqrDist = math.lengthsq(bulletPos - swarmerPositions[sj].Value);
                        if (sqrDist <= sqrExplosion)
                        {
                            var splashRef = EntityManager.GetComponentObject<SwarmerCompanionRef>(swarmerEntities[sj]);
                            if (splashRef != null && splashRef.MB != null)
                                splashRef.MB.TakeDamage(data.ValueRO.ExplosionDamage);
                        }
                    }
                }

                // Direct hit damage.
                swarmerRef.MB.TakeDamage(data.ValueRO.Damage);

                data.ValueRW.Penetration--;
                if (data.ValueRO.Penetration <= 0)
                {
                    dead.ValueRW = true;
                    break;
                }
            }
        }

        swarmerEntities.Dispose();
        swarmerPositions.Dispose();
        swarmerRadii.Dispose();
    }
}
