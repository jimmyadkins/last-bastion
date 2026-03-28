using Unity.Entities;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Copies BulletPosition (ECS) → companion Bullet.transform.position each tick
/// so the visual GO tracks the ECS-simulated bullet.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletTransformSyncSystem : SystemBase
{
    protected override void OnUpdate()
    {
        foreach (var (pos, companionRef) in
            SystemAPI.Query<RefRO<BulletPosition>, BulletCompanionRef>()
            .WithDisabled<BulletDeadTag>())
        {
            if (companionRef.MB != null)
                companionRef.MB.transform.position = pos.ValueRO.Value;
        }
    }
}
