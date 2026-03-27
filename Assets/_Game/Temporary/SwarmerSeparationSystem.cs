using Latios;
using Latios.Psyshock;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Phase 3: replaces the O(n²) Burst SeparationJob with Psyshock FindPairs broadphase.
///
/// Detection spheres are inflated to NeighborDetectionDistance/2 so FindPairs reports
/// every pair within NeighborDetectionDistance. A parallel Radii array carries the
/// actual swarmer radius for the original separation formula, which gives a
/// "comfort zone" at the touching distance (2*radius).
/// </summary>
[BurstCompile]
public partial struct SwarmerSeparationSystem : ISystem, ISystemNewScene
{
    private EntityQuery m_query;

    public void OnNewScene(ref SystemState state) { }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_query = SystemAPI.QueryBuilder()
            .WithAll<SwarmerPosition, SwarmerRadius, SwarmerSeparation>()
            .Build();
        // No RequireForUpdate — Latios RootSuperSystem manages scheduling directly.
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var wbb = state.GetLatiosWorldUnmanaged().worldBlackboardEntity;
        if (!wbb.HasComponent<SwarmerConfig>()) return;
        var config = wbb.GetComponentData<SwarmerConfig>();
        int count  = m_query.CalculateEntityCount();
        if (count == 0) return;

        float detectionRadius = config.NeighborDetectionDistance * 0.5f;

        // ── Step 1: Build ColliderBody array (inflated spheres) + actual Radii array ─
        var bodies = CollectionHelper.CreateNativeArray<ColliderBody>(count, state.WorldUpdateAllocator);
        var radii  = CollectionHelper.CreateNativeArray<float>(count,        state.WorldUpdateAllocator);

        new BuildBodiesJob
        {
            Bodies          = bodies,
            Radii           = radii,
            DetectionRadius = detectionRadius,
        }.Schedule(m_query, state.Dependency).Complete();

        // ── Step 2: Build Psyshock CollisionLayer ──────────────────────────────────
        Physics.BuildCollisionLayer(bodies)
            .ScheduleParallel(out CollisionLayer layer, state.WorldUpdateAllocator, default)
            .Complete();

        // ── Step 3: FindPairs — accumulate per-entity separation contributions ──────
        // Use a flat NativeArray<float3> indexed by entity-in-query index.
        // ScheduleSingle keeps it single-threaded so we can accumulate directly
        // without atomics or a hash map, eliminating the "HashMap is full" error
        // that occurs under dense clustering (worst-case O(n²) pairs).
        var separations = CollectionHelper.CreateNativeArray<float3>(count, state.WorldUpdateAllocator);

        var processor = new SeparationProcessor
        {
            Separations = separations,
            Radii       = radii,
        };

        Physics.FindPairs(in layer, in layer, in processor)
            .ScheduleSingle(default)
            .Complete();

        // ── Step 4: Write accumulated separations → SwarmerSeparation ────────────
        new ReduceContributionsJob
        {
            Separations = separations,
        }.Schedule(m_query, default).Complete();

        state.Dependency = default;
    }

    // ─── Jobs ─────────────────────────────────────────────────────────────────────

    [BurstCompile]
    private partial struct BuildBodiesJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<ColliderBody> Bodies;
        [NativeDisableParallelForRestriction] public NativeArray<float>        Radii;
        public float DetectionRadius;

        public void Execute(
            [EntityIndexInQuery] int index,
            in SwarmerPosition pos,
            in SwarmerRadius radius,
            Entity entity)
        {
            Bodies[index] = new ColliderBody
            {
                collider  = new SphereCollider(float3.zero, DetectionRadius),
                transform = new TransformQvvs(pos.Value, quaternion.identity),
                entity    = entity,
            };
            Radii[index] = radius.Value;
        }
    }

    [BurstCompile]
    private struct SeparationProcessor : IFindPairsProcessor
    {
        // Direct array indexed by sourceIndex — safe because ScheduleSingle is single-threaded.
        [NativeDisableParallelForRestriction] public NativeArray<float3> Separations;
        [ReadOnly]                            public NativeArray<float>   Radii;

        public void Execute(in FindPairsResult result)
        {
            float3 posA   = result.transformA.position;
            float3 posB   = result.transformB.position;
            float3 diff   = posA - posB;
            float  distSq = math.lengthsq(diff);

            if (distSq < 0.0001f) return;

            float dist   = math.sqrt(distSq);
            float radius = Radii[result.sourceIndexA];

            // Original formula: maximum push at touching distance (dist == 2*radius),
            // falls off in both directions. Matches the pre-ECS SeparationJob behavior.
            float denom = math.max(math.abs(dist - 2f * radius), 0.03f) * 4f;
            float scale = 1f / (denom * denom);
            float3 sep  = diff * scale;

            Separations[result.sourceIndexA] += sep;
            Separations[result.sourceIndexB] -= sep;
        }
    }

    [BurstCompile]
    private partial struct ReduceContributionsJob : IJobEntity
    {
        [ReadOnly] public NativeArray<float3> Separations;

        public void Execute(
            [EntityIndexInQuery] int index,
            ref SwarmerSeparation separation)
        {
            separation.Value = Separations[index];
        }
    }
}
