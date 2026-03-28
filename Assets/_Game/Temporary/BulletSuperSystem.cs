using Latios;
using Unity.Entities;

/// <summary>
/// Root super-system for all bullet ECS logic.
/// Runs inside FixedStepSimulationSystemGroup to match the physics tick rate.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial class BulletSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
        GetOrCreateAndAddManagedSystem<BulletSpawnSystem>();       // create entities from pending requests
        GetOrCreateAndAddUnmanagedSystem<BulletMoveSystem>();      // Burst: move + lifetime
        GetOrCreateAndAddManagedSystem<BulletWallHitSystem>();     // raycast sweep, ricochet
        GetOrCreateAndAddManagedSystem<BulletSwarmerHitSystem>();  // distance check, damage
        GetOrCreateAndAddManagedSystem<BulletTransformSyncSystem>(); // ECS pos → companion transform
        GetOrCreateAndAddManagedSystem<BulletDeathSystem>();       // VFX, pool return, entity destroy
    }
}
