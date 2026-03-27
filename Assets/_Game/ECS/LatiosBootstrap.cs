using Latios;
using Unity.Entities;

/// <summary>
/// Custom ECS world bootstrap using Latios Framework.
/// Replaces Unity's default world initialization with a LatiosWorld,
/// enabling BlackboardEntity, SuperSystems, collection components,
/// and Psyshock physics.
/// </summary>
[UnityEngine.Scripting.Preserve]
public class LatiosBootstrap : ICustomBootstrap
{
    public bool Initialize(string defaultWorldName)
    {
        var world = new LatiosWorld(defaultWorldName);
        World.DefaultGameObjectInjectionWorld = world;

        // Discover and register all systems (including FixedStepSimulationSystemGroup
        // and SwarmerSuperSystem) into the world's root-level system groups.
        var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);

        // Hook the world into Unity's player loop so Update/FixedUpdate/etc. fire.
        ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);

        world.initializationSystemGroup.SortSystems();
        return true;
    }
}
