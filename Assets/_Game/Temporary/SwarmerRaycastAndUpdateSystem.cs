using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Phase 5: replaces the raycast batch + SyncToECS + UpdateSwarmer loop that
/// previously lived in SwarmerManager.FixedUpdate.
///
/// Runs first in SwarmerSuperSystem so avoidance data is fresh before
/// SwarmerSteeringSystem reads SwarmerAvoidanceInput / SwarmerTargetPos.
///
/// [DisableAutoCreation] — managed by SwarmerSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class SwarmerRaycastAndUpdateSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = GetEntityQuery(ComponentType.ReadOnly<SwarmerCompanionRef>());
        RequireForUpdate<SwarmerConfig>();
    }

    protected override void OnUpdate()
    {
        var sm = SwarmerManager.Instance;
        if (sm == null) return;

        // Collect live MB references from ECS companion refs
        var entities = m_query.ToEntityArray(Allocator.Temp);
        int total    = entities.Length;
        if (total == 0) { entities.Dispose(); return; }

        var swarmers = new SwarmerController[total];
        int count    = 0;
        for (int i = 0; i < total; i++)
        {
            var cr = EntityManager.GetComponentObject<SwarmerCompanionRef>(entities[i]);
            if (cr?.MB != null) swarmers[count++] = cr.MB;
        }
        entities.Dispose();
        if (count == 0) return;

        // ── Batch raycast pass ──────────────────────────────────────────────
        const int RaysPerSwarmer = 7;
        int mainMask = (1 << Defines.EnvironmentLayer) | (1 << Defines.PlayerLayer);
        var mainParams    = new QueryParameters(mainMask);
        var whiskerParams = new QueryParameters(Physics.DefaultRaycastLayers);

        var rayCommands = new NativeArray<RaycastCommand>(count * RaysPerSwarmer, Allocator.TempJob);
        var rayResults  = new NativeArray<RaycastHit>   (count * RaysPerSwarmer, Allocator.TempJob);

        for (int i = 0; i < count; i++)
        {
            SwarmerController s  = swarmers[i];
            Vector3    pos       = s.transform.position;
            Vector3    fwd       = s.transform.forward;
            float      r        = s.Radius;
            Quaternion avoidRot  = Quaternion.AngleAxis(sm.AvoidanceAngle, Vector3.up);

            int b = i * RaysPerSwarmer;
            rayCommands[b + 0] = new RaycastCommand(pos,                         fwd,                                mainParams,    sm.ObstacleAvoidDistance);
            rayCommands[b + 1] = new RaycastCommand(pos + s.transform.right * r, avoidRot * fwd,                     mainParams,    sm.ObstacleAvoidDistance);
            rayCommands[b + 2] = new RaycastCommand(pos - s.transform.right * r, Quaternion.Inverse(avoidRot) * fwd, mainParams,    sm.ObstacleAvoidDistance);

            for (int w = 0; w < 2; w++)
            {
                float      angle = 45f + w * 45f;
                Quaternion rot   = Quaternion.AngleAxis(angle, Vector3.up);
                rayCommands[b + 3 + w * 2]     = new RaycastCommand(pos, Quaternion.Inverse(rot) * fwd, whiskerParams, sm.WhiskerDistance);
                rayCommands[b + 3 + w * 2 + 1] = new RaycastCommand(pos, rot * fwd,                     whiskerParams, sm.WhiskerDistance);
            }
        }

        RaycastCommand.ScheduleBatch(rayCommands, rayResults, 32).Complete();

        for (int i = 0; i < count; i++)
        {
            int b = i * RaysPerSwarmer;
            swarmers[i].SetPrecomputedRaycasts(
                rayResults[b + 0], rayResults[b + 1], rayResults[b + 2],
                rayResults[b + 3], rayResults[b + 5],   // left whiskers 0, 1
                rayResults[b + 4], rayResults[b + 6]);  // right whiskers 0, 1
        }

        rayCommands.Dispose();
        rayResults.Dispose();

        // ── Sync MB → ECS, then run MB-side avoidance + target logic ───────
        for (int i = 0; i < count; i++) swarmers[i].SyncToECS();
        for (int i = 0; i < count; i++)
        {
            swarmers[i].UpdateSwarmer();
            sm.UpdateSwarmerGridPosition(swarmers[i]);
        }
    }
}
