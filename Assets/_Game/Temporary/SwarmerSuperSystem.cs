using Latios;
using Unity.Entities;

/// <summary>
/// Root super-system for all swarmer ECS logic.
/// Runs inside FixedStepSimulationSystemGroup so physics timing is respected.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial class SwarmerSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
        GetOrCreateAndAddUnmanagedSystem<SwarmerGridUpdateSystem>();
        GetOrCreateAndAddUnmanagedSystem<SwarmerSeparationSystem>();
        GetOrCreateAndAddUnmanagedSystem<SwarmerSteeringSystem>();
        // Phase 4: ECS owns kinematic integration — no Rigidbody.
        GetOrCreateAndAddUnmanagedSystem<SwarmerMoveApplySystem>();
        // Phase 4: write ECS position/velocity/heading → companion Transform each tick.
        GetOrCreateAndAddManagedSystem<SwarmerTransformSyncSystem>();
        GetOrCreateAndAddManagedSystem<SwarmerAttackSystem>();
        GetOrCreateAndAddManagedSystem<SwarmerDeathSystem>();
    }
}
