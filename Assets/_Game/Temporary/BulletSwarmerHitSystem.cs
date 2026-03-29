using Latios.Psyshock;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Phase C: replaces the O(n·m) scan with Psyshock FindPairs broadphase.
///
/// Per bullet: a CapsuleCollider from BulletPrevPosition to BulletPosition is
/// used so fast bullets (railgun 500 u/s) can't tunnel through swarmers between
/// ticks. Swarmers use their existing SwarmerRadius sphere.
///
/// Flow per tick:
///   1. Build ColliderBody arrays (Burst, parallel).
///   2. Build two CollisionLayers (Burst, parallel).
///   3. FindPairs(bulletLayer, swarmerLayer) → NativeList of (bulletIdx, swarmerIdx).
///   4. Main thread: apply damage, decrement penetration, mark dead.
///
/// AoE splash is still handled by BulletDeathSystem on bullet death.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletSwarmerHitSystem : SystemBase
{
    private EntityQuery m_bulletQuery;
    private EntityQuery m_swarmerQuery;

    private struct HitPair { public int BulletIndex; public int SwarmerIndex; }

    protected override void OnCreate()
    {
        m_bulletQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BulletPosition, BulletPrevPosition, BulletData>()
            .WithDisabled<BulletDeadTag>()
            .Build(this);

        m_swarmerQuery = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerRadius>(),
            ComponentType.ReadWrite<SwarmerHealth>());
    }

    protected override void OnUpdate()
    {
        int bulletCount  = m_bulletQuery.CalculateEntityCount();
        int swarmerCount = m_swarmerQuery.CalculateEntityCount();
        if (bulletCount == 0 || swarmerCount == 0) return;

        // ── Snapshot component data ──────────────────────────────────────────
        var bulletPositions  = m_bulletQuery.ToComponentDataArray<BulletPosition>(Allocator.TempJob);
        var bulletPrevPos    = m_bulletQuery.ToComponentDataArray<BulletPrevPosition>(Allocator.TempJob);
        var bulletDatas      = m_bulletQuery.ToComponentDataArray<BulletData>(Allocator.TempJob);
        var bulletEntities   = m_bulletQuery.ToEntityArray(Allocator.TempJob);
        var swarmerPositions = m_swarmerQuery.ToComponentDataArray<SwarmerPosition>(Allocator.TempJob);
        var swarmerRadii     = m_swarmerQuery.ToComponentDataArray<SwarmerRadius>(Allocator.TempJob);
        var swarmerEntities  = m_swarmerQuery.ToEntityArray(Allocator.TempJob);

        // ── Build ColliderBody arrays ────────────────────────────────────────
        var bulletBodies  = new NativeArray<ColliderBody>(bulletCount,  Allocator.TempJob);
        var swarmerBodies = new NativeArray<ColliderBody>(swarmerCount, Allocator.TempJob);

        new BuildBulletBodiesJob
        {
            Positions     = bulletPositions,
            PrevPositions = bulletPrevPos,
            Datas         = bulletDatas,
            Entities      = bulletEntities,
            Bodies        = bulletBodies,
        }.Schedule(bulletCount, 64).Complete();

        new BuildSwarmerBodiesJob
        {
            Positions = swarmerPositions,
            Radii     = swarmerRadii,
            Entities  = swarmerEntities,
            Bodies    = swarmerBodies,
        }.Schedule(swarmerCount, 64).Complete();

        // ── Build Psyshock CollisionLayers ───────────────────────────────────
        Physics.BuildCollisionLayer(bulletBodies)
            .ScheduleParallel(out CollisionLayer bulletLayer, Allocator.TempJob, default)
            .Complete();

        Physics.BuildCollisionLayer(swarmerBodies)
            .ScheduleParallel(out CollisionLayer swarmerLayer, Allocator.TempJob, default)
            .Complete();

        // ── FindPairs → collect hits ─────────────────────────────────────────
        var hits = new NativeList<HitPair>(64, Allocator.TempJob);

        Physics.FindPairs(in bulletLayer, in swarmerLayer, new HitCollector { Hits = hits })
            .ScheduleSingle(default)
            .Complete();

        // ── Apply damage on main thread ──────────────────────────────────────
        for (int i = 0; i < hits.Length; i++)
        {
            Entity bulletEntity  = bulletEntities[hits[i].BulletIndex];
            Entity swarmerEntity = swarmerEntities[hits[i].SwarmerIndex];

            // Bullet may have been killed by an earlier hit this frame.
            if (EntityManager.IsComponentEnabled<BulletDeadTag>(bulletEntity)) continue;

            var data   = EntityManager.GetComponentData<BulletData>(bulletEntity);
            var health = EntityManager.GetComponentData<SwarmerHealth>(swarmerEntity);
            health.Current -= data.Damage;
            EntityManager.SetComponentData(swarmerEntity, health);

            data.Penetration--;
            EntityManager.SetComponentData(bulletEntity, data);
            if (data.Penetration <= 0)
                EntityManager.SetComponentEnabled<BulletDeadTag>(bulletEntity, true);
        }

        // ── Cleanup ──────────────────────────────────────────────────────────
        hits.Dispose();
        bulletLayer.Dispose();
        swarmerLayer.Dispose();
        bulletBodies.Dispose();
        swarmerBodies.Dispose();
        bulletPositions.Dispose();
        bulletPrevPos.Dispose();
        bulletDatas.Dispose();
        bulletEntities.Dispose();
        swarmerPositions.Dispose();
        swarmerRadii.Dispose();
        swarmerEntities.Dispose();
    }

    // ── Jobs ──────────────────────────────────────────────────────────────────

    [BurstCompile]
    private struct BuildBulletBodiesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BulletPosition>     Positions;
        [ReadOnly] public NativeArray<BulletPrevPosition> PrevPositions;
        [ReadOnly] public NativeArray<BulletData>         Datas;
        [ReadOnly] public NativeArray<Entity>             Entities;
        public            NativeArray<ColliderBody>       Bodies;

        public void Execute(int i)
        {
            float3 prev = PrevPositions[i].Value;
            float3 cur  = Positions[i].Value;

            // Capsule from prevPos to curPos sweeps the full path this tick.
            Bodies[i] = new ColliderBody
            {
                collider  = new CapsuleCollider(float3.zero, cur - prev, Datas[i].ColliderRadius),
                transform = new TransformQvvs(prev, quaternion.identity),
                entity    = Entities[i],
            };
        }
    }

    [BurstCompile]
    private struct BuildSwarmerBodiesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SwarmerPosition> Positions;
        [ReadOnly] public NativeArray<SwarmerRadius>   Radii;
        [ReadOnly] public NativeArray<Entity>          Entities;
        public            NativeArray<ColliderBody>    Bodies;

        public void Execute(int i)
        {
            Bodies[i] = new ColliderBody
            {
                collider  = new SphereCollider(float3.zero, Radii[i].Value),
                transform = new TransformQvvs(Positions[i].Value, quaternion.identity),
                entity    = Entities[i],
            };
        }
    }

    [BurstCompile]
    private struct HitCollector : IFindPairsProcessor
    {
        public NativeList<HitPair> Hits;

        public void Execute(in FindPairsResult result)
        {
            Hits.Add(new HitPair
            {
                BulletIndex  = result.sourceIndexA,
                SwarmerIndex = result.sourceIndexB,
            });
        }
    }
}
