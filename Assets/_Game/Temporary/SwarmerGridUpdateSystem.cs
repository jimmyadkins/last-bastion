using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Iterates all swarmer entities, updates EnemyGridCell coordinates, and
/// accumulates per-cell heading averages + counts into the world-blackboard
/// SwarmerGridData collection component.
///
/// Runs single-threaded (Schedule) so we can write into a plain NativeHashMap
/// without race conditions. Still Burst-compiled — much faster than managed C#.
/// </summary>
[BurstCompile]
public partial struct SwarmerGridUpdateSystem : ISystem, ISystemNewScene
{
    private LatiosWorldUnmanaged m_latiosWorld;
    private EntityQuery          m_swarmerQuery;

    public void OnNewScene(ref SystemState state) { }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_swarmerQuery = SystemAPI.QueryBuilder()
            .WithAll<SwarmerPosition, SwarmerHeading, EnemyGridCell>()
            .Build();
        // No RequireForUpdate — Latios RootSuperSystem manages scheduling directly.
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        m_latiosWorld = state.GetLatiosWorldUnmanaged();
        var wbb = m_latiosWorld.worldBlackboardEntity;
        if (!wbb.HasComponent<SwarmerConfig>()) return;
        var config = wbb.GetComponentData<SwarmerConfig>();

        int count    = m_swarmerQuery.CalculateEntityCount();
        int capacity = math.max(64, count * 2); // 2x for hash collision headroom

        // Temp maps for accumulation (single-threaded job writes into these)
        var cellHeadings = new NativeHashMap<int2, float2>(capacity, state.WorldUpdateAllocator);
        var cellCounts   = new NativeHashMap<int2, int>(capacity,    state.WorldUpdateAllocator);

        new GridAccumulateJob
        {
            CellSize     = config.CellSize,
            CellHeadings = cellHeadings,
            CellCounts   = cellCounts,
        }.Schedule(state.Dependency).Complete();   // Complete so we can iterate on main thread below

        // Normalise heading vectors
        var keys = cellHeadings.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < keys.Length; i++)
        {
            int2 k = keys[i];
            cellHeadings[k] = math.normalizesafe(cellHeadings[k]);
        }
        keys.Dispose();

        // Build NativeParallelHashMap so the steering system can read concurrently
        int headsCount = cellHeadings.Count;
        int cellsCount = cellCounts.Count;
        var parallelHeadings = new NativeParallelHashMap<int2, float2>(
            math.max(1, headsCount), state.WorldUpdateAllocator);
        var parallelCounts = new NativeParallelHashMap<int2, int>(
            math.max(1, cellsCount), state.WorldUpdateAllocator);

        var hKeys = cellHeadings.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < hKeys.Length; i++)
        {
            parallelHeadings[hKeys[i]] = cellHeadings[hKeys[i]];
        }
        hKeys.Dispose();

        var cKeys = cellCounts.GetKeyArray(Allocator.Temp);
        for (int i = 0; i < cKeys.Length; i++)
        {
            parallelCounts[cKeys[i]] = cellCounts[cKeys[i]];
        }
        cKeys.Dispose();

        // Push to blackboard (disposes old collection component automatically)
        m_latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new SwarmerGridData
        {
            CellHeadings = parallelHeadings,
            CellCounts   = parallelCounts,
        });
    }

    [BurstCompile]
    private partial struct GridAccumulateJob : IJobEntity
    {
        public float                       CellSize;
        public NativeHashMap<int2, float2> CellHeadings;
        public NativeHashMap<int2, int>    CellCounts;

        public void Execute(ref EnemyGridCell cell, in SwarmerPosition pos, in SwarmerHeading heading)
        {
            int2 coord = WorldToCell(pos.Value, CellSize);
            cell.Coord = coord;

            // Accumulate heading (normalised on main thread afterwards)
            if (CellHeadings.TryGetValue(coord, out float2 existing))
                CellHeadings[coord] = existing + heading.Forward.xz;
            else
                CellHeadings[coord] = heading.Forward.xz;

            // Count
            if (CellCounts.TryGetValue(coord, out int cnt))
                CellCounts[coord] = cnt + 1;
            else
                CellCounts[coord] = 1;
        }

        private static int2 WorldToCell(float3 pos, float cellSize)
            => new int2((int)math.floor(pos.x / cellSize), (int)math.floor(pos.z / cellSize));
    }
}
