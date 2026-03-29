using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Processes all bullets with BulletDeadTag enabled (dead this frame):
///   1. AoE splash damage to nearby swarmers if ExplosionRadius > 0.
///   2. Triggers explosion VFX + audio via Bullet.TriggerDeathFX().
///   3. Returns companion GO to BulletPool.
///   4. Destroys the ECS entity via ECB.
///
/// Handling AoE here (rather than BulletSwarmerHitSystem) means explosions
/// trigger regardless of death cause: swarmer hit, ground hit, or lifetime expiry.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletDeathSystem : SystemBase
{
    private EntityQuery m_deadQuery;
    private EntityQuery m_swarmerQuery;

    protected override void OnCreate()
    {
        m_deadQuery = GetEntityQuery(
            ComponentType.ReadOnly<BulletPosition>(),
            ComponentType.ReadOnly<BulletData>(),
            ComponentType.ReadOnly<BulletCompanionRef>(),
            ComponentType.ReadOnly<BulletDeadTag>());

        m_swarmerQuery = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());
    }

    protected override void OnUpdate()
    {
        var entities = m_deadQuery.ToEntityArray(Allocator.Temp);
        if (entities.Length == 0)
        {
            entities.Dispose();
            return;
        }

        // Snapshot swarmer state once for any AoE checks this tick.
        var swarmerEntities  = m_swarmerQuery.ToEntityArray(Allocator.Temp);
        var swarmerPositions = m_swarmerQuery.ToComponentDataArray<SwarmerPosition>(Allocator.Temp);

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];

            var bulletPos    = EntityManager.GetComponentData<BulletPosition>(e);
            var bulletData   = EntityManager.GetComponentData<BulletData>(e);
            var companionRef = EntityManager.GetComponentObject<BulletCompanionRef>(e);

            // AoE splash — applies on any death cause (ground hit, swarmer hit, lifetime).
            if (bulletData.ExplosionRadius > 0f)
            {
                float sqrRadius = bulletData.ExplosionRadius * bulletData.ExplosionRadius;
                for (int si = 0; si < swarmerEntities.Length; si++)
                {
                    if (math.lengthsq(bulletPos.Value - swarmerPositions[si].Value) > sqrRadius) continue;
                    var sr = EntityManager.GetComponentObject<SwarmerCompanionRef>(swarmerEntities[si]);
                    if (sr?.MB != null)
                        sr.MB.TakeDamage(bulletData.ExplosionDamage);
                }
            }

            if (companionRef?.MB != null)
            {
                Bullet companion = companionRef.MB;
                companion.TriggerDeathFX(bulletPos.Value);

                if (BulletPool.Instance != null && companion.PoolPrefab != null)
                    BulletPool.Instance.Return(companion.PoolPrefab, companion);
                else
                    Object.Destroy(companion.gameObject);
            }

            ecb.DestroyEntity(e);
        }

        swarmerEntities.Dispose();
        swarmerPositions.Dispose();
        entities.Dispose();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
