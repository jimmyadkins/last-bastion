using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Main-thread managed system.
/// Reads SwarmerDesiredVelocity written by the Burst steering job and applies
/// it as a Rigidbody impulse via the companion MonoBehaviour.
///
/// Managed by SwarmerSuperSystem — [DisableAutoCreation] prevents Unity from
/// registering it a second time in the default groups.
/// </summary>
[DisableAutoCreation]
public partial class SwarmerMoveApplySystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = GetEntityQuery(
            ComponentType.ReadWrite<SwarmerPosition>(),
            ComponentType.ReadOnly<SwarmerDesiredVelocity>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());

        RequireForUpdate<SwarmerConfig>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<SwarmerConfig>();

        var entities     = m_query.ToEntityArray(Allocator.Temp);
        var positions    = m_query.ToComponentDataArray<SwarmerPosition>(Allocator.Temp);
        var desiredVels  = m_query.ToComponentDataArray<SwarmerDesiredVelocity>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var companion = EntityManager.GetComponentObject<SwarmerCompanionRef>(entities[i]);
            SwarmerController mb = companion?.MB;
            if (mb == null) continue;

            Rigidbody rb = mb.GetRigidbody();
            if (rb == null) continue;

            Vector3 currentV = rb.linearVelocity;
            var     dv3      = desiredVels[i].Value;
            Vector3 desired  = new Vector3(dv3.x, dv3.y, dv3.z);

            Vector3 dv = Vector3.ClampMagnitude(desired - currentV, config.Acceleration);
            rb.AddForce(dv, ForceMode.Impulse);

            // Rotate toward desired heading
            if (desired.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(desired.normalized, Vector3.up);
                mb.transform.rotation = Quaternion.RotateTowards(
                    mb.transform.rotation, targetRot,
                    config.TurnSpeed * SystemAPI.Time.fixedDeltaTime);
            }

            // Write back updated position
            var p = mb.transform.position;
            positions[i] = new SwarmerPosition { Value = new Unity.Mathematics.float3(p.x, p.y, p.z) };
        }

        // Flush positions back
        m_query.CopyFromComponentDataArray(positions);

        entities.Dispose();
        positions.Dispose();
        desiredVels.Dispose();
    }
}
