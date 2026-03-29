using Unity.Entities;
using Unity.Collections;

/// <summary>
/// Runs first in BulletSuperSystem. Drains BulletSpawner.PendingSpawns and
/// creates one ECS entity per request with all bullet components attached.
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletSpawnSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (BulletSpawner.Instance == null) return;

        var pending = BulletSpawner.Instance.PendingSpawns;
        if (pending.Count == 0) return;

        foreach (var req in pending)
        {
            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new BulletPosition     { Value = req.Position });
            EntityManager.AddComponentData(e, new BulletPrevPosition { Value = req.Position });
            EntityManager.AddComponentData(e, new BulletVelocity     { Value = req.Velocity });
            EntityManager.AddComponentData(e, req.Data);
            EntityManager.AddComponentObject(e, new BulletCompanionRef { MB = req.Companion });
            EntityManager.AddComponent<BulletDeadTag>(e);
            EntityManager.SetComponentEnabled<BulletDeadTag>(e, false); // disabled = alive
        }

        pending.Clear();
    }
}
