using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Phase 4: writes ECS-authoritative position and velocity back to the companion
/// MonoBehaviour each fixed tick, after SwarmerMoveApplySystem integrates position.
///
/// - transform.position ← SwarmerPosition (ECS owns movement now)
/// - transform.rotation ← SwarmerHeading  (ECS steering owns heading)
/// - LastKnownVelocity  ← SwarmerVelocity (read by TurretController for ballistic prediction)
///
/// Managed by SwarmerSuperSystem — [DisableAutoCreation] prevents double-registration.
/// </summary>
[DisableAutoCreation]
public partial class SwarmerTransformSyncSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerVelocity>(),
            ComponentType.ReadOnly<SwarmerHeading>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());

        RequireForUpdate<SwarmerConfig>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<SwarmerConfig>();
        float dt   = SystemAPI.Time.DeltaTime;

        var entities   = m_query.ToEntityArray(Allocator.Temp);
        var positions  = m_query.ToComponentDataArray<SwarmerPosition>(Allocator.Temp);
        var velocities = m_query.ToComponentDataArray<SwarmerVelocity>(Allocator.Temp);
        var headings   = m_query.ToComponentDataArray<SwarmerHeading>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var companionRef = EntityManager.GetComponentObject<SwarmerCompanionRef>(entities[i]);
            SwarmerController mb = companionRef?.MB;
            if (mb == null) continue;

            float3 p = positions[i].Value;
            mb.transform.position = new Vector3(p.x, p.y, p.z);

            float3 v = velocities[i].Value;
            mb.LastKnownVelocity = new Vector3(v.x, v.y, v.z);

            float3 fwd = headings[i].Forward;
            if (math.lengthsq(fwd) > 0.001f)
            {
                // Heading is already smoothly rotated by SwarmerSteeringSystem — set directly.
                mb.transform.rotation = Quaternion.LookRotation(
                    new Vector3(fwd.x, fwd.y, fwd.z), Vector3.up);
            }
        }

        entities.Dispose();
        positions.Dispose();
        velocities.Dispose();
        headings.Dispose();
    }
}
