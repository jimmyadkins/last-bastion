using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Phase 5: replaces the HQ collision-damage + swarmer-destroy block that
/// previously lived in SwarmerManager.FixedUpdate.
///
/// Runs after SwarmerTransformSyncSystem (positions synced) and before
/// SwarmerDeathSystem (which cleans up entities whose companion GO was destroyed).
///
/// [DisableAutoCreation] — managed by SwarmerSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class SwarmerHQDamageSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());
        RequireForUpdate<SwarmerConfig>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<SwarmerConfig>();
        var target = SwarmerTarget.Instance;
        if (target == null) return;

        float3 targetPos      = new float3(target.transform.position.x, target.transform.position.y, target.transform.position.z);
        float  destroyDistSq  = config.TargetDestroyDistance * config.TargetDestroyDistance;
        HQ     hq             = target.GetComponentInParent<HQ>();

        var entities  = m_query.ToEntityArray(Allocator.Temp);
        var positions = m_query.ToComponentDataArray<SwarmerPosition>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            if (math.distancesq(targetPos, positions[i].Value) >= destroyDistSq) continue;

            hq?.TakeCollisionDamage(config.CollisionDamage);

            var cr = EntityManager.GetComponentObject<SwarmerCompanionRef>(entities[i]);
            if (cr?.MB != null)
                Object.Destroy(cr.MB.gameObject);
            // SwarmerDeathSystem detects the null MB next tick and destroys the entity.
        }

        entities.Dispose();
        positions.Dispose();
    }
}
