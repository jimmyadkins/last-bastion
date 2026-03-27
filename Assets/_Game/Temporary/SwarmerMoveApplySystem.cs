using Latios;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Phase 4: pure kinematic ECS integration — no Rigidbody.
/// Reads SwarmerDesiredVelocity written by SwarmerSteeringSystem, clamps the
/// velocity delta to Config.Acceleration, and integrates SwarmerPosition.
/// SwarmerTransformSyncSystem (runs after) writes the result to companion Transform.
/// </summary>
[BurstCompile]
public partial struct SwarmerMoveApplySystem : ISystem, ISystemNewScene
{
    public void OnNewScene(ref SystemState state) { }

    [BurstCompile]
    public void OnCreate(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var wbb = state.GetLatiosWorldUnmanaged().worldBlackboardEntity;
        if (!wbb.HasComponent<SwarmerConfig>()) return;
        var config = wbb.GetComponentData<SwarmerConfig>();

        state.Dependency = new IntegrateJob
        {
            Config    = config,
            DeltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct IntegrateJob : IJobEntity
    {
        public SwarmerConfig Config;
        public float         DeltaTime;

        public void Execute(
            ref SwarmerPosition        pos,
            ref SwarmerVelocity        vel,
            in  SwarmerDesiredVelocity desired)
        {
            float3 dv    = desired.Value - vel.Value;
            float  dvLen = math.length(dv);
            if (dvLen > Config.Acceleration)
                dv *= Config.Acceleration / dvLen;

            vel.Value   += dv;
            vel.Value   *= math.max(0f, 1f - Config.LinearDamping * DeltaTime);
            vel.Value.y  = 0f;           // enforce flat movement — no gravity without Rigidbody
            pos.Value   += vel.Value * DeltaTime;
        }
    }
}
