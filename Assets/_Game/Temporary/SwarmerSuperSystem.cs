using Latios;
using Unity.Entities;

/// <summary>
/// Root super-system for all Phase-2 swarmer ECS logic.
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
        // SwarmerMoveApplySystem disabled: MB still owns Rigidbody movement in Phase 3.
        // Re-enable when Phase 4 removes the Rigidbody and ECS owns kinematic integration.
        // GetOrCreateAndAddManagedSystem<SwarmerMoveApplySystem>();
        GetOrCreateAndAddManagedSystem<SwarmerAttackSystem>();
        GetOrCreateAndAddManagedSystem<SwarmerDeathSystem>();
    }
}
