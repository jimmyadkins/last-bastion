using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Copies BulletPosition (ECS) → companion Bullet.transform.position each tick
/// so the visual GO tracks the ECS-simulated bullet.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletTransformSyncSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BulletPosition, BulletCompanionRef>()
            .WithDisabled<BulletDeadTag>()
            .Build(this);
    }

    protected override void OnUpdate()
    {
        var entities  = m_query.ToEntityArray(Allocator.Temp);
        var positions = m_query.ToComponentDataArray<BulletPosition>(Allocator.Temp);

        if (entities.Length > 0)
            UnityEngine.Debug.Log($"[BulletTransformSync] Syncing {entities.Length} alive bullets");

        for (int i = 0; i < entities.Length; i++)
        {
            var cr = EntityManager.GetComponentObject<BulletCompanionRef>(entities[i]);
            if (cr?.MB != null)
                cr.MB.transform.position = positions[i].Value;
        }

        entities.Dispose();
        positions.Dispose();
    }
}
