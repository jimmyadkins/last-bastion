using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Managed system: integrates bullet positions each fixed tick.
/// Saves prevPos for wall-hit sweep, applies gravity, decrements lifetime,
/// and marks expired bullets dead via SetComponentEnabled.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletMoveSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = new EntityQueryBuilder(Allocator.Temp)
            .WithAllRW<BulletPosition>()
            .WithAllRW<BulletPrevPosition>()
            .WithAllRW<BulletVelocity>()
            .WithAllRW<BulletData>()
            .WithDisabled<BulletDeadTag>()
            .Build(this);
    }

    protected override void OnUpdate()
    {
        float dt      = SystemAPI.Time.DeltaTime;
        float gravity = UnityEngine.Physics.gravity.y;

        var entities = m_query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var e    = entities[i];
            var pos  = EntityManager.GetComponentData<BulletPosition>(e);
            var prev = EntityManager.GetComponentData<BulletPrevPosition>(e);
            var vel  = EntityManager.GetComponentData<BulletVelocity>(e);
            var data = EntityManager.GetComponentData<BulletData>(e);

            prev.Value = pos.Value;

            if (data.AffectedByGravity)
                vel.Value.y += gravity * dt;

            pos.Value     += vel.Value * dt;
            data.Lifetime -= dt;

            EntityManager.SetComponentData(e, pos);
            EntityManager.SetComponentData(e, prev);
            EntityManager.SetComponentData(e, vel);
            EntityManager.SetComponentData(e, data);

            if (data.Lifetime <= 0f)
                EntityManager.SetComponentEnabled<BulletDeadTag>(e, true);
        }

        entities.Dispose();
    }
}
