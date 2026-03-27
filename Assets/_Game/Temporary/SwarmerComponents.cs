using Latios;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;

// ─── Kinematic state ────────────────────────────────────────────────────────
public struct SwarmerPosition : IComponentData { public float3 Value; }
public struct SwarmerVelocity : IComponentData { public float3 Value; }
public struct SwarmerHeading  : IComponentData { public float3 Forward; }

// ─── Physical size ───────────────────────────────────────────────────────────
public struct SwarmerRadius : IComponentData { public float Value; }

// ─── Grid assignment ─────────────────────────────────────────────────────────
public struct EnemyGridCell : IComponentData { public int2 Coord; }

// ─── Targeting (managed state lives in companion MB; this caches the position) ─
public struct SwarmerTargetPos : IComponentData
{
    public float3 Value;
    public bool   HasTarget;
    public bool   IsFacingTarget;
    public bool   IsHQTarget;
}

// ─── Avoidance (written by SwarmerController each tick, before ECS runs) ─────
public struct SwarmerAvoidanceInput : IComponentData
{
    // -1 = turn left, 0 = no avoidance, 1 = turn right.
    // ECS computes the world-space direction from SwarmerHeading so it's never stale.
    public int   AvoidanceRotation;
    public float Strength;
    public bool  ShouldSlowDown;
}

// ─── Steering output ─────────────────────────────────────────────────────────
// Written by SwarmerSteeringSystem, read by SwarmerMoveApplySystem.
public struct SwarmerDesiredVelocity : IComponentData { public float3 Value; }

// ─── Separation ──────────────────────────────────────────────────────────────
// Written by Phase-1 Burst job in SwarmerManager, read by SwarmerSteeringSystem.
public struct SwarmerSeparation : IComponentData { public float3 Value; }

// ─── Attack flag ─────────────────────────────────────────────────────────────
public struct SwarmerIsAttacking : IComponentData { public bool Value; }

// ─── Companion link (managed class IComponentData → links entity back to MB) ─
public class SwarmerCompanionRef : IComponentData
{
    public SwarmerController MB;
}

// ─── Global config stored on worldBlackboardEntity ───────────────────────────
public struct SwarmerConfig : IComponentData
{
    public float MaxSpeed;
    public float Acceleration;
    public float TurnSpeed;
    public float TargetWeight;
    public float AlignmentWeight;
    public float SeparationWeight;
    public float ObstacleWeight;
    public float NeighborDetectionDistance;
    public float AttackDistance;
    public float CellSize;
    public float TargetDestroyDistance;
    public float LinearDamping;
    public int   AttackDamage;
    public int   CollisionDamage;
}

// ─── Grid data stored on worldBlackboardEntity (ICollectionComponent) ────────
// Must be `partial` so Latios source generators can emit the required
// ICollectionComponentSourceGenerated implementation.
public partial struct SwarmerGridData : ICollectionComponent
{
    /// <summary>Normalised average heading (xz) per EnemyGrid cell.</summary>
    public NativeParallelHashMap<int2, float2> CellHeadings;
    /// <summary>Swarmer count per EnemyGrid cell.</summary>
    public NativeParallelHashMap<int2, int>    CellCounts;

    public JobHandle TryDispose(JobHandle inputDeps)
    {
        var h1 = CellHeadings.IsCreated ? CellHeadings.Dispose(inputDeps) : inputDeps;
        var h2 = CellCounts.IsCreated   ? CellCounts.Dispose(h1)          : h1;
        return h2;
    }
}
