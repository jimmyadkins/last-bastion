using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Sweeps a ray from BulletPrevPosition to BulletPosition each tick.
/// On environment hit: ricochets (angle &lt;= RicochetAngle) or marks bullet dead.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletWallHitSystem : SystemBase
{
    private static readonly int k_hitMask =
        (1 << Defines.EnvironmentLayer) | (1 << Defines.PlayerLayer);

    protected override void OnUpdate()
    {
        foreach (var (pos, prevPos, vel, dead) in
            SystemAPI.Query<
                RefRW<BulletPosition>,
                RefRO<BulletPrevPosition>,
                RefRW<BulletVelocity>,
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

            Vector3 normal = hit.normal;
            float angle = Vector3.Angle(normal, -(Vector3)vel.ValueRO.Value.normalized) - 90f;

            if (angle <= Defines.RicochetAngle)
            {
                // Reflect velocity and snap position to hit point.
                float3 reflected = math.reflect(vel.ValueRO.Value, (float3)normal);
                vel.ValueRW.Value = reflected;
                pos.ValueRW.Value = (float3)hit.point + (float3)normal * 0.01f; // small offset to avoid re-hit
            }
            else
            {
                dead.ValueRW = true;
            }
        }
    }
}
