using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Phase 1 of ECS migration: Burst-compiled separation job.
/// Replaces per-swarmer Physics.OverlapSphere neighbor loops with a single
/// parallel Burst job that reads all positions and writes separation vectors.
/// Raycasts and all other MonoBehaviour logic remain unchanged.
/// </summary>
public static class SwarmerBurstJobs
{
    [BurstCompile]
    public struct SeparationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<bool>   IsAttacking;
        public float NeighborDistanceSq;
        public float Radius;
        [WriteOnly] public NativeArray<float3> OutSeparation;

        public void Execute(int i)
        {
            float3 pos = Positions[i];
            float3 sep = float3.zero;
            int len = Positions.Length;

            for (int j = 0; j < len; j++)
            {
                if (i == j || IsAttacking[j])
                    continue;

                float3 diff   = pos - Positions[j];
                float  distSq = math.lengthsq(diff);

                if (distSq > NeighborDistanceSq)
                    continue;

                float dist   = math.sqrt(distSq);
                float denom  = math.max(math.abs(dist - 2f * Radius), 0.03f) * 4f;
                float scale  = 1f / (denom * denom);
                sep += diff * scale;
            }

            OutSeparation[i] = sep;
        }
    }
}
