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
        // Phase 5: raycast batch + SyncToECS + UpdateSwarmer — runs first so
        // avoidance/target data is fresh before steering reads it.
        GetOrCreateAndAddManagedSystem<SwarmerRaycastAndUpdateSystem>();
        GetOrCreateAndAddUnmanagedSystem<SwarmerGridUpdateSystem>();
        GetOrCreateAndAddUnmanagedSystem<SwarmerSeparationSystem>();
        GetOrCreateAndAddUnmanagedSystem<SwarmerSteeringSystem>();
        GetOrCreateAndAddUnmanagedSystem<SwarmerMoveApplySystem>();
        GetOrCreateAndAddManagedSystem<SwarmerTransformSyncSystem>();
        GetOrCreateAndAddManagedSystem<SwarmerAttackSystem>();
        // Phase 5: HQ collision — after positions synced, before entity death cleanup.
        GetOrCreateAndAddManagedSystem<SwarmerHQDamageSystem>();
        GetOrCreateAndAddManagedSystem<SwarmerDeathSystem>();
    }
}
