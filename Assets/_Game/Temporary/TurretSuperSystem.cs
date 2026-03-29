using Latios;
using Unity.Entities;

/// <summary>
/// Runs TurretTargetingSystem each fixed tick, after swarmer grid data is
/// updated, so CellCounts/CellHeadings are fresh for arty density scoring.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(SwarmerSuperSystem))]
public partial class TurretSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
        GetOrCreateAndAddManagedSystem<TurretTargetingSystem>();
    }
}
