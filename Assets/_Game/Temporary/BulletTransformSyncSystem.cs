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
            if (cr?.MB == null) continue;

            UnityEngine.Vector3 newPos = positions[i].Value;
            cr.MB.transform.position = newPos;

            // If a Rigidbody is still present, sync its physics position too so
            // PhysX doesn't snap the GO back on the next FixedUpdate.
            if (cr.MB.TryGetComponent<UnityEngine.Rigidbody>(out var rb))
                rb.position = newPos;
        }

        entities.Dispose();
        positions.Dispose();
    }
}
