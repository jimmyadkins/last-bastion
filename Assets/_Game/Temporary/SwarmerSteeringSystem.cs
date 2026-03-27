using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Burst IJobEntity that reads per-entity state (position, heading, separation,
/// avoidance, target) plus the blackboard cell-heading map and writes a
/// desired-velocity and updated heading for each swarmer.
///
/// SwarmerMoveApplySystem reads SwarmerDesiredVelocity on the main thread
/// and applies it via the companion Rigidbody.
/// </summary>
[BurstCompile]
public partial struct SwarmerSteeringSystem : ISystem, ISystemNewScene
{
    private LatiosWorldUnmanaged m_latiosWorld;
    private EntityQuery          m_query;

    public void OnNewScene(ref SystemState state) { }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_query = SystemAPI.QueryBuilder()
            .WithAll<SwarmerPosition, SwarmerHeading, SwarmerTargetPos,
                     SwarmerSeparation, SwarmerAvoidanceInput, EnemyGridCell,
                     SwarmerDesiredVelocity>()
            .Build();
        // No RequireForUpdate — Latios RootSuperSystem manages scheduling directly.
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        m_latiosWorld = state.GetLatiosWorldUnmanaged();
        var wbb = m_latiosWorld.worldBlackboardEntity;
        if (!wbb.HasComponent<SwarmerConfig>()) return;
        var config   = wbb.GetComponentData<SwarmerConfig>();
        if (!wbb.HasCollectionComponent<SwarmerGridData>()) return;
        var gridData = wbb.GetCollectionComponent<SwarmerGridData>(readOnly: true);

        state.Dependency = new SteeringJob
        {
            Config       = config,
            CellHeadings = gridData.CellHeadings,
            DeltaTime    = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct SteeringJob : IJobEntity
    {
        [ReadOnly] public SwarmerConfig                        Config;
        [ReadOnly] public NativeParallelHashMap<int2, float2>  CellHeadings;
        public float                                           DeltaTime;

        public void Execute(
            in  SwarmerPosition        pos,
            ref SwarmerHeading         heading,         // read current, write updated
            in  SwarmerTargetPos       target,
            in  SwarmerSeparation      separation,
            in  SwarmerAvoidanceInput  avoidance,
            in  EnemyGridCell          cell,
            ref SwarmerDesiredVelocity desiredVelocity)
        {
            float2 fwd = heading.Forward.xz;

            // --- Target direction (xz) ---
            float2 toTarget    = target.Value.xz - pos.Value.xz;
            float  distToTarget = math.length(toTarget);
            float2 toTargetN   = distToTarget > 0.001f ? toTarget / distToTarget : fwd;

            // --- Alignment from cell average ---
            float2 alignment = float2.zero;
            if (CellHeadings.TryGetValue(cell.Coord, out float2 cellHeading))
                alignment = cellHeading;

            // --- Separation (Phase-1 Burst job writes this; synced to ECS each tick) ---
            float2 sep = separation.Value.xz;

            // Derive avoidance direction from current ECS heading so it's never stale.
            // In Unity XZ: right-perpendicular of forward (fx, fz) = (fz, -fx).
            float2 headingRight = new float2(fwd.y, -fwd.x); // fwd is xz float2, so .y == world-Z
            float2 avoidDir     = headingRight * avoidance.AvoidanceRotation;
            float  avoidStr     = avoidance.Strength;

            // --- Desired heading blend ---
            float2 desired = toTargetN  * Config.TargetWeight    +
                             alignment  * Config.AlignmentWeight  +
                             avoidDir   * (avoidStr * Config.ObstacleWeight) +
                             sep        * Config.SeparationWeight;

            float  desiredLen = math.length(desired);
            float2 desiredN   = desiredLen > 0.001f ? desired / desiredLen : fwd;

            // --- Desired speed ---
            float speed = math.lerp(1f, Config.MaxSpeed, math.saturate(distToTarget / 5f));

            bool inAttackBlock = false;

            if (target.HasTarget && !target.IsHQTarget)
            {
                float2 toTgt = target.Value.xz - pos.Value.xz;
                float  dist  = math.length(toTgt);
                if (dist <= Config.AttackDistance * 1.5f)
                {
                    inAttackBlock = true;
                    desiredN = dist > 0.001f ? toTgt / dist : fwd;
                    float offset = dist - Config.AttackDistance;
                    // Deadband: hold position within ±10% of AttackDistance.
                    // Only approach — never actively back off (separation handles spacing).
                    const float kDeadbandFraction = 0.1f;
                    float deadband = Config.AttackDistance * kDeadbandFraction;
                    speed = offset > deadband
                        ? math.clamp(0.5f * offset, 0f, 1f) * Config.MaxSpeed
                        : 0f;
                }
            }
            else if (target.IsHQTarget)
            {
                float2 toHQ = target.Value.xz - pos.Value.xz;
                float  d    = math.length(toHQ);
                desiredN = d > 0.001f ? toHQ / d : fwd;
                speed    = Config.MaxSpeed;
            }

            // Don't slow down while attacking — the swarmer is supposed to be next to its target.
            if (avoidance.ShouldSlowDown && !inAttackBlock)
                speed *= 0.1f;

            // Rotate heading toward desired direction at TurnSpeed — matches old RotateTowards behaviour.
            // Velocity then follows the NEW heading, not the raw blend, so competing side-forces
            // (e.g. both walls in a tunnel) don't flip velocity direction every frame.
            float maxAngle   = math.radians(Config.TurnSpeed * DeltaTime);
            float cosAngle   = math.clamp(math.dot(fwd, desiredN), -1f, 1f);
            float angle      = math.acos(cosAngle);
            float2 newFwd    = angle < 0.0001f
                ? desiredN
                : math.normalizesafe(math.lerp(fwd, desiredN, math.min(1f, maxAngle / angle)));

            float3 newDir   = new float3(newFwd.x, 0f, newFwd.y);
            desiredVelocity = new SwarmerDesiredVelocity { Value = newDir * speed };

            // Update heading for next frame's grid accumulation
            heading = new SwarmerHeading { Forward = newDir };
        }
    }
}
