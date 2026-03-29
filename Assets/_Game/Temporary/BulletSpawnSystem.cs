using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

/// <summary>
/// Runs first in BulletSuperSystem. Drains BulletSpawner.PendingSpawns and
/// creates one ECS entity per request with all bullet components attached.
///
/// Entities Graphics: if the companion GO has a MeshRenderer + MeshFilter,
/// RenderMeshUtility.AddComponents sets up GPU-instanced rendering on the ECS
/// entity. After removing MeshRenderer from the bullet prefabs in the editor,
/// the companion GO becomes a trail-only proxy and the mesh is rendered here.
///
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

            // ── Entities Graphics rendering ──────────────────────────────────
            // Set up GPU-instanced rendering from the companion GO's mesh/material.
            // TODO: remove MeshRenderer + MeshFilter from bullet prefabs in the editor
            //       once this is confirmed working — the companion GO then becomes a
            //       trail-only proxy.
            var mf = req.Companion != null ? req.Companion.GetComponent<UnityEngine.MeshFilter>()  : null;
            var mr = req.Companion != null ? req.Companion.GetComponent<UnityEngine.MeshRenderer>() : null;
            if (mf != null && mr != null && mf.sharedMesh != null && mr.sharedMaterial != null)
            {
                var renderMeshArray = new RenderMeshArray(
                    new[] { mr.sharedMaterial },
                    new[] { mf.sharedMesh });

                RenderMeshUtility.AddComponents(e, EntityManager,
                    new RenderMeshDescription(ShadowCastingMode.On),
                    renderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
            }
            else
            {
                // No mesh on companion (already removed in editor) — just add LocalToWorld
                // so BulletMoveSystem can update orientation without a null-check.
                EntityManager.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
            }

            // Set initial LocalToWorld (RenderMeshUtility added it above).
            EntityManager.SetComponentData(e, BulletL2W(req.Position, req.Velocity, req.Data.MeshScale));
        }

        pending.Clear();
    }

    internal static LocalToWorld BulletL2W(float3 pos, float3 vel, float scale)
    {
        quaternion rot = math.lengthsq(vel) > 0.0001f
            ? quaternion.LookRotationSafe(math.normalize(vel), new float3(0f, 1f, 0f))
            : quaternion.identity;
        return new LocalToWorld { Value = float4x4.TRS(pos, rot, new float3(scale, scale, scale)) };
    }
}
