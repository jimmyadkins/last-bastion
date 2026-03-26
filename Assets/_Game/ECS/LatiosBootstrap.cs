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
        world.initializationSystemGroup.SortSystems();
        return true;
    }
}
