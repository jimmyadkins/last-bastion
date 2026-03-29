using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class TurretManager : MonoBehaviour
{
    public static TurretManager Instance { get; private set; }

    private List<TurretController> m_reactionTurrets = new();
    private List<TurretController> m_otherTurrets    = new();
    private List<ArtyController>   m_artillery       = new();

    private EntityManager m_entityManager;

    protected void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
            m_entityManager = world.EntityManager;
    }

    public void Register(TurretController turret)
    {
        if (IsReactionTurret(turret))
            m_reactionTurrets.Add(turret);
        else if (turret is ArtyController arty)
            m_artillery.Add(arty);
        else
            m_otherTurrets.Add(turret);
    }

    public void Deregister(TurretController turret)
    {
        if (IsReactionTurret(turret))
            m_reactionTurrets.Remove(turret);
        else if (turret is ArtyController arty)
            m_artillery.Remove(arty);
        else
            m_otherTurrets.Remove(turret);
    }

    private bool IsReactionTurret(TurretController turret)
    {
        GameObject go = turret.gameObject;
        return
            go.TryGetComponent(typeof(Gatling), out _) ||
            go.TryGetComponent(typeof(Railgun), out _);
    }

    protected void FixedUpdate()
    {
        PushTurretRegistry();
        ApplyECSAssignments();

        foreach (var turret in m_otherTurrets)
            turret.UpdateTurret();
        foreach (var turret in m_reactionTurrets)
            turret.UpdateTurret();
        foreach (var turret in m_artillery)
            turret.UpdateTurret();
    }

    // ── ECS bridge ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes current turret state to TurretTargetingSystem.Registry so the ECS
    /// system can assign targets this Update tick (results arrive next FixedUpdate).
    /// </summary>
    private void PushTurretRegistry()
    {
        int regularCount = m_otherTurrets.Count + m_reactionTurrets.Count;
        int totalCount   = regularCount + m_artillery.Count;

        TurretTargetingSystem.RegularCount = regularCount;

        if (TurretTargetingSystem.Registry.Length != totalCount)
            TurretTargetingSystem.Registry = new TurretTargetingSystem.TurretEntry[totalCount];

        int idx = 0;
        foreach (var t in m_otherTurrets)
            TurretTargetingSystem.Registry[idx++] = new TurretTargetingSystem.TurretEntry
            {
                Position  = (float3)t.transform.position,
                Range     = t.detectionRange,
                HasTarget = t.HasTarget,
            };
        foreach (var t in m_reactionTurrets)
            TurretTargetingSystem.Registry[idx++] = new TurretTargetingSystem.TurretEntry
            {
                Position  = (float3)t.transform.position,
                Range     = t.detectionRange,
                HasTarget = t.HasTarget,
            };
        foreach (var a in m_artillery)
            TurretTargetingSystem.Registry[idx++] = new TurretTargetingSystem.TurretEntry
            {
                Position        = (float3)a.transform.position,
                Range           = a.detectionRange,
                ExplosionRadius = a.BulletExplosionRadius,
                BulletSpeed     = a.BulletSpeed,
                IsArty          = true,
                HasTarget       = a.HasTarget,
            };
    }

    /// <summary>
    /// Reads TurretTargetingSystem output written last ECS tick and applies target
    /// assignments to turrets that still need one.
    /// </summary>
    private void ApplyECSAssignments()
    {
        var regularAssignments = TurretTargetingSystem.RegularAssignments;
        if (regularAssignments.Length == 0) return;

        int idx = 0;
        for (int i = 0; i < m_otherTurrets.Count && idx < regularAssignments.Length; i++, idx++)
        {
            Entity e = regularAssignments[idx];
            if (e == Entity.Null || m_otherTurrets[i].HasTarget) continue;
            var sc = TryGetSwarmerController(e);
            if (sc != null) m_otherTurrets[i].AssignTarget(sc);
        }
        for (int i = 0; i < m_reactionTurrets.Count && idx < regularAssignments.Length; i++, idx++)
        {
            Entity e = regularAssignments[idx];
            if (e == Entity.Null || m_reactionTurrets[i].HasTarget) continue;
            var sc = TryGetSwarmerController(e);
            if (sc != null) m_reactionTurrets[i].AssignTarget(sc);
        }

        var artyHas = TurretTargetingSystem.ArtyHasAssignment;
        var artyPos = TurretTargetingSystem.ArtyAimPositions;
        for (int i = 0; i < m_artillery.Count && i < artyHas.Length; i++)
        {
            if (artyHas[i] && !m_artillery[i].HasTarget)
                m_artillery[i].AssignTarget(artyPos[i]);
        }
    }

    private SwarmerController TryGetSwarmerController(Entity e)
    {
        if (!m_entityManager.Exists(e)) return null;
        var cr = m_entityManager.GetComponentObject<SwarmerCompanionRef>(e);
        return cr?.MB;
    }
}
