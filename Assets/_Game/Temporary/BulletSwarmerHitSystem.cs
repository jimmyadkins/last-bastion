using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Main-thread managed system.
/// For each alive bullet, checks distance against all alive swarmer positions.
/// Applies direct damage and decrements penetration; marks bullet dead when exhausted.
/// AoE splash is handled by BulletDeathSystem so it fires on any death cause
/// (swarmer hit, ground hit, lifetime expiry).
///
/// Phase C will replace this O(n·m) scan with Psyshock FindPairs.
///
/// [DisableAutoCreation] — managed by BulletSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class BulletSwarmerHitSystem : SystemBase
{
    private EntityQuery m_swarmerQuery;

    protected override void OnCreate()
    {
        m_swarmerQuery = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerRadius>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());
    }

    protected override void OnUpdate()
    {
        var swarmerEntities  = m_swarmerQuery.ToEntityArray(Allocator.Temp);
        var swarmerPositions = m_swarmerQuery.ToComponentDataArray<SwarmerPosition>(Allocator.Temp);
        var swarmerRadii     = m_swarmerQuery.ToComponentDataArray<SwarmerRadius>(Allocator.Temp);

        foreach (var (pos, prevPos, data, dead) in
            SystemAPI.Query<
                RefRO<BulletPosition>,
                RefRO<BulletPrevPosition>,
                RefRW<BulletData>,
                EnabledRefRW<BulletDeadTag>>()
            .WithDisabled<BulletDeadTag>())
        {
            float3 a = prevPos.ValueRO.Value; // start of this frame's sweep
            float3 b = pos.ValueRO.Value;     // end of this frame's sweep
            float3 ab = b - a;
            float abLenSq = math.lengthsq(ab);

            for (int si = 0; si < swarmerEntities.Length; si++)
            {
                // Closest point on segment (a→b) to swarmer center.
                float3 ap = swarmerPositions[si].Value - a;
                float t = abLenSq > 0f ? math.clamp(math.dot(ap, ab) / abLenSq, 0f, 1f) : 0f;
                float3 closest = a + t * ab;
                float dist = math.distance(closest, swarmerPositions[si].Value);

                if (dist > data.ValueRO.ColliderRadius + swarmerRadii[si].Value) continue;

                var swarmerRef = EntityManager.GetComponentObject<SwarmerCompanionRef>(swarmerEntities[si]);
                if (swarmerRef?.MB == null) continue;

                swarmerRef.MB.TakeDamage(data.ValueRO.Damage);

                data.ValueRW.Penetration--;
                if (data.ValueRO.Penetration <= 0)
                {
                    dead.ValueRW = true;
                    break;
                }
            }
        }

        swarmerEntities.Dispose();
        swarmerPositions.Dispose();
        swarmerRadii.Dispose();
    }
}
