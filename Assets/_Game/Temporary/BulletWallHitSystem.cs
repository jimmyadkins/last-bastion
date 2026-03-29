using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Sweeps a ray from BulletPrevPosition to BulletPosition each tick.
/// On environment hit:
///   - If CanRicochet and impact is shallow (impactAngle > RicochetAngle): ricochet.
///   - Otherwise: mark bullet dead (triggers AoE explosion via BulletDeathSystem).
///
/// impactAngle = Vector3.Angle(surface_normal, -velocity_direction).
/// 0° = head-on (perpendicular), 90° = grazing (parallel to surface).
/// RicochetAngle = 80° means only very shallow grazes ricochet.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletWallHitSystem : SystemBase
{
    // Block on environment/obstacles and the Default layer (floor/ground planes).
    // User-placed walls (PlayerLayer) are intentionally passed through.
    private static readonly int k_hitMask = (1 << Defines.EnvironmentLayer) | (1 << 0);

    protected override void OnUpdate()
    {
        foreach (var (pos, prevPos, vel, data, dead) in
            SystemAPI.Query<
                RefRW<BulletPosition>,
                RefRO<BulletPrevPosition>,
                RefRW<BulletVelocity>,
                RefRO<BulletData>,
                EnabledRefRW<BulletDeadTag>>()
            .WithDisabled<BulletDeadTag>())
        {
            Vector3 from = prevPos.ValueRO.Value;
            Vector3 to   = pos.ValueRW.Value;
            Vector3 disp = to - from;
            float   dist = disp.magnitude;

            if (dist < 0.0001f) continue;

            if (!Physics.Raycast(from, disp / dist, out RaycastHit hit, dist, k_hitMask))
                continue;

            Vector3 normal      = hit.normal;
            float   impactAngle = Vector3.Angle(normal, -(Vector3)math.normalize(vel.ValueRO.Value));
            // impactAngle: 0° = head-on, 90° = grazing.
            // Ricochet only for very shallow grazing impacts AND if this bullet type can ricochet.
            if (data.ValueRO.CanRicochet && impactAngle > Defines.RicochetAngle)
            {
                float3 reflected = math.reflect(vel.ValueRO.Value, (float3)normal);
                vel.ValueRW.Value = reflected;
                pos.ValueRW.Value = (float3)hit.point + (float3)normal * 0.01f;
            }
            else
            {
                dead.ValueRW = true;
            }
        }
    }
}
