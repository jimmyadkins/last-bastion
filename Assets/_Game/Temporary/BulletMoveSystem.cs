using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Burst-compiled IJobEntity.
/// • Saves current position to BulletPrevPosition (used by wall-hit sweep).
/// • Applies gravity to velocity (AffectedByGravity bullets).
/// • Integrates position.
/// • Decrements lifetime; sets BulletDeadTag enabled when expired.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial struct BulletMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt      = SystemAPI.Time.DeltaTime;
        float gravity = -9.81f; // standard; match Physics.gravity.y if needed

        state.Dependency = new MoveJob { DeltaTime = dt, GravityY = gravity }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithDisabled(typeof(BulletDeadTag))]
    partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;
        public float GravityY;

        void Execute(
            ref BulletPosition     pos,
            ref BulletPrevPosition prevPos,
            ref BulletVelocity     vel,
            ref BulletData         data,
            EnabledRefRW<BulletDeadTag> dead)
        {
            prevPos.Value = pos.Value;

            if (data.AffectedByGravity)
                vel.Value.y += GravityY * DeltaTime;

            pos.Value     += vel.Value * DeltaTime;
            data.Lifetime -= DeltaTime;

            if (data.Lifetime <= 0f)
                dead.ValueRW = true;
        }
    }
}
