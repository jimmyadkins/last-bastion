using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Processes all bullets with BulletDeadTag enabled (dead this frame):
///   1. Triggers explosion VFX + audio via Bullet.TriggerDeathFX().
///   2. Returns companion GO to BulletPool.
///   3. Destroys the ECS entity via ECB.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletDeathSystem : SystemBase
{
    private EntityQuery m_deadQuery;

    protected override void OnCreate()
    {
        m_deadQuery = GetEntityQuery(
            ComponentType.ReadOnly<BulletPosition>(),
            ComponentType.ReadOnly<BulletCompanionRef>(),
            ComponentType.ReadOnly<BulletDeadTag>());
    }

    protected override void OnUpdate()
    {
        var entities = m_deadQuery.ToEntityArray(Allocator.Temp);
        if (entities.Length == 0)
        {
            entities.Dispose();
            return;
        }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity e = entities[i];

            var bulletPos    = EntityManager.GetComponentData<BulletPosition>(e);
            var companionRef = EntityManager.GetComponentObject<BulletCompanionRef>(e);

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

        entities.Dispose();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
