using System;
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Replaces per-turret OverlapSphere + grid search with a single Burst pass
/// over all swarmer positions.
///
/// Regular turrets: nearest unclaimed swarmer within range (sequential IJob,
/// NativeHashSet prevents double-assignment).
///
/// Artillery: density-grid scoring (counts swarmers within explosion radius of
/// each candidate cell) + lead prediction (cell heading × maxSpeed × flightTime).
/// Multiple arty units pick non-overlapping clusters.
///
/// Static bridge pattern: TurretManager pushes Registry each FixedUpdate;
/// this system runs during ECS Update (1 frame later) and writes results;
/// TurretManager reads results on the next FixedUpdate.
///
/// [DisableAutoCreation] — managed by TurretSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class TurretTargetingSystem : SystemBase
{
    // ── Static bridge ─────────────────────────────────────────────────────────

    public struct TurretEntry
    {
        public float3 Position;
        public float  Range;
        public float  ExplosionRadius; // arty only
        public float  BulletSpeed;     // arty only (for flight-time estimate)
        public bool   IsArty;
        public bool   HasTarget;
    }

    /// Written by TurretManager.FixedUpdate() each tick.
    /// Layout: [otherTurrets | reactionTurrets | artillery]
    public static TurretEntry[] Registry     = Array.Empty<TurretEntry>();
    /// Count of non-arty entries at the start of Registry.
    public static int           RegularCount = 0;

    /// Written by this system, read by TurretManager next FixedUpdate.
    public static Entity[]  RegularAssignments = Array.Empty<Entity>();
    public static bool[]    ArtyHasAssignment  = Array.Empty<bool>();
    public static Vector3[] ArtyAimPositions   = Array.Empty<Vector3>();

    // ─────────────────────────────────────────────────────────────────────────

    private EntityQuery m_swarmerQuery;

    protected override void OnCreate()
    {
        m_swarmerQuery = GetEntityQuery(ComponentType.ReadOnly<SwarmerPosition>());
    }

    protected override void OnUpdate()
    {
        var registry = Registry;
        if (registry.Length == 0) return;

        int swarmerCount = m_swarmerQuery.CalculateEntityCount();
        int regularCount = RegularCount;
        int artyCount    = registry.Length - regularCount;

        // Resize output arrays when turret topology changes.
        if (RegularAssignments.Length != regularCount)
            RegularAssignments = new Entity[regularCount];
        if (ArtyHasAssignment.Length != artyCount)
        {
            ArtyHasAssignment = new bool[artyCount];
            ArtyAimPositions  = new Vector3[artyCount];
        }

        if (swarmerCount == 0)
        {
            for (int i = 0; i < regularCount; i++) RegularAssignments[i] = Entity.Null;
            for (int i = 0; i < artyCount; i++)    ArtyHasAssignment[i]  = false;
            return;
        }

        var swarmerPositions = m_swarmerQuery.ToComponentDataArray<SwarmerPosition>(Allocator.TempJob);
        var swarmerEntities  = m_swarmerQuery.ToEntityArray(Allocator.TempJob);

        // ── Regular targeting ─────────────────────────────────────────────────
        if (regularCount > 0)
        {
            var turretPos    = new NativeArray<float3>(regularCount, Allocator.TempJob);
            var turretRange  = new NativeArray<float>(regularCount, Allocator.TempJob);
            var turretHasTgt = new NativeArray<bool>(regularCount, Allocator.TempJob);
            var swarmPos     = new NativeArray<float3>(swarmerCount, Allocator.TempJob);
            var results      = new NativeArray<int>(regularCount, Allocator.TempJob);

            for (int i = 0; i < regularCount; i++)
            {
                turretPos[i]    = registry[i].Position;
                turretRange[i]  = registry[i].Range;
                turretHasTgt[i] = registry[i].HasTarget;
            }
            for (int i = 0; i < swarmerCount; i++)
                swarmPos[i] = swarmerPositions[i].Value;

            new RegularTargetingJob
            {
                TurretPositions  = turretPos,
                TurretRanges     = turretRange,
                TurretHasTarget  = turretHasTgt,
                SwarmerPositions = swarmPos,
                Assignments      = results,
            }.Schedule().Complete();

            for (int i = 0; i < regularCount; i++)
                RegularAssignments[i] = results[i] >= 0 ? swarmerEntities[results[i]] : Entity.Null;

            turretPos.Dispose();
            turretRange.Dispose();
            turretHasTgt.Dispose();
            swarmPos.Dispose();
            results.Dispose();
        }

        // ── Arty targeting ────────────────────────────────────────────────────
        if (artyCount > 0)
        {
            var latiosWorld = World.Unmanaged.GetLatiosWorldUnmanaged();
            var wbb         = latiosWorld.worldBlackboardEntity;

            if (!wbb.HasCollectionComponent<SwarmerGridData>() || !wbb.HasComponent<SwarmerConfig>())
            {
                for (int i = 0; i < artyCount; i++) ArtyHasAssignment[i] = false;
            }
            else
            {
                var gridData = wbb.GetCollectionComponent<SwarmerGridData>(readOnly: true);
                var config   = wbb.GetComponentData<SwarmerConfig>();

                var cellPairs  = gridData.CellCounts.GetKeyValueArrays(Allocator.TempJob);
                var artyIn     = new NativeArray<ArtyJobInput>(artyCount, Allocator.TempJob);
                var artyAim    = new NativeArray<float3>(artyCount, Allocator.TempJob);
                var artyHas    = new NativeArray<bool>(artyCount, Allocator.TempJob);

                for (int i = 0; i < artyCount; i++)
                {
                    var e = registry[regularCount + i];
                    artyIn[i] = new ArtyJobInput
                    {
                        Position        = e.Position,
                        Range           = e.Range,
                        ExplosionRadius = e.ExplosionRadius,
                        BulletSpeed     = e.BulletSpeed,
                        HasTarget       = e.HasTarget,
                    };
                }

                new ArtyTargetingJob
                {
                    Arty           = artyIn,
                    CellKeys       = cellPairs.Keys,
                    CellCountMap   = gridData.CellCounts,
                    CellHeadingMap = gridData.CellHeadings,
                    CellSize       = config.CellSize,
                    Gravity        = math.abs(Physics.gravity.y),
                    SwarmerMaxSpeed = config.MaxSpeed,
                    AimPositions   = artyAim,
                    HasAssignment  = artyHas,
                }.Schedule().Complete();

                wbb.UpdateMainThreadAccess<SwarmerGridData>(wasReadOnly: true);

                for (int i = 0; i < artyCount; i++)
                {
                    ArtyHasAssignment[i] = artyHas[i];
                    ArtyAimPositions[i]  = artyAim[i];
                }

                cellPairs.Dispose();
                artyIn.Dispose();
                artyAim.Dispose();
                artyHas.Dispose();
            }
        }

        swarmerPositions.Dispose();
        swarmerEntities.Dispose();
    }

    // ── Jobs ──────────────────────────────────────────────────────────────────

    [BurstCompile]
    private struct RegularTargetingJob : IJob
    {
        [ReadOnly] public NativeArray<float3> TurretPositions;
        [ReadOnly] public NativeArray<float>  TurretRanges;
        [ReadOnly] public NativeArray<bool>   TurretHasTarget;
        [ReadOnly] public NativeArray<float3> SwarmerPositions;
        public NativeArray<int> Assignments;

        public void Execute()
        {
            var claimed = new NativeHashSet<int>(SwarmerPositions.Length, Allocator.Temp);

            for (int ti = 0; ti < TurretPositions.Length; ti++)
            {
                Assignments[ti] = -1;
                if (TurretHasTarget[ti]) continue;

                float rangeSq    = TurretRanges[ti] * TurretRanges[ti];
                int   bestIdx    = -1;
                float bestDistSq = float.MaxValue;

                for (int si = 0; si < SwarmerPositions.Length; si++)
                {
                    if (claimed.Contains(si)) continue;
                    float distSq = math.distancesq(TurretPositions[ti], SwarmerPositions[si]);
                    if (distSq <= rangeSq && distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIdx    = si;
                    }
                }

                Assignments[ti] = bestIdx;
                if (bestIdx >= 0) claimed.Add(bestIdx);
            }

            claimed.Dispose();
        }
    }

    private struct ArtyJobInput
    {
        public float3 Position;
        public float  Range;
        public float  ExplosionRadius;
        public float  BulletSpeed;
        public bool   HasTarget;
    }

    [BurstCompile]
    private struct ArtyTargetingJob : IJob
    {
        [ReadOnly] public NativeArray<ArtyJobInput>               Arty;
        [ReadOnly] public NativeArray<int2>                       CellKeys;
        [ReadOnly] public NativeParallelHashMap<int2, int>        CellCountMap;
        [ReadOnly] public NativeParallelHashMap<int2, float2>     CellHeadingMap;
        public float CellSize;
        public float Gravity;
        public float SwarmerMaxSpeed;

        public NativeArray<float3> AimPositions;
        public NativeArray<bool>   HasAssignment;

        public void Execute()
        {
            var claimedCells = new NativeHashSet<int2>(16, Allocator.Temp);

            for (int ai = 0; ai < Arty.Length; ai++)
            {
                HasAssignment[ai] = false;
                if (Arty[ai].HasTarget) continue;

                float  rangeSq    = Arty[ai].Range * Arty[ai].Range;
                float3 artyPos    = Arty[ai].Position;
                int    blastCells = (int)math.ceil(Arty[ai].ExplosionRadius / CellSize);
                float  halfCell   = CellSize * 0.5f;

                int  bestScore = 0;
                int2 bestCell  = default;

                for (int ci = 0; ci < CellKeys.Length; ci++)
                {
                    int2   cell       = CellKeys[ci];
                    float3 cellCenter = new float3(cell.x * CellSize + halfCell, 0f, cell.y * CellSize + halfCell);

                    if (math.distancesq(artyPos, cellCenter) > rangeSq) continue;
                    if (claimedCells.Contains(cell)) continue;

                    // Blast score: sum counts in all cells within explosion radius.
                    int score = 0;
                    for (int dx = -blastCells; dx <= blastCells; dx++)
                    for (int dz = -blastCells; dz <= blastCells; dz++)
                    {
                        if (CellCountMap.TryGetValue(new int2(cell.x + dx, cell.y + dz), out int c))
                            score += c;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCell  = cell;
                    }
                }

                if (bestScore == 0) continue;

                claimedCells.Add(bestCell);

                // Lead-predict: offset aim point by heading × maxSpeed × flight time.
                float3 cellCenter3 = new float3(bestCell.x * CellSize + halfCell, 0f, bestCell.y * CellSize + halfCell);
                float  flightTime  = math.sqrt(2f) * Arty[ai].BulletSpeed / Gravity;
                float2 heading     = CellHeadingMap.TryGetValue(bestCell, out float2 h) ? h : float2.zero;
                float3 aimPos      = cellCenter3 + new float3(heading.x, 0f, heading.y) * SwarmerMaxSpeed * flightTime;

                AimPositions[ai]  = aimPos;
                HasAssignment[ai] = true;
            }

            claimedCells.Dispose();
        }
    }
}
