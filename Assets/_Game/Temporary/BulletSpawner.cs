using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Phase B singleton.  TurretController.Fire() calls Spawn() instead of
/// BulletPool.Rent() directly.  Spawn() rents the visual companion from the
/// pool and queues an ECS entity creation request that BulletSpawnSystem
/// drains at the start of each BulletSuperSystem update.
/// </summary>
public class BulletSpawner : MonoBehaviour
{
    public static BulletSpawner Instance { get; private set; }

    internal struct SpawnRequest
    {
        public Bullet   Companion;  // already rented from BulletPool, active in scene
        public float3   Position;
        public float3   Velocity;
        public BulletData Data;
    }

    internal readonly List<SpawnRequest> PendingSpawns = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Rents a companion GO from BulletPool and queues an ECS entity to be
    /// created by BulletSpawnSystem on the next system update.
    /// </summary>
    public void Spawn(Bullet prefab, Vector3 pos, Quaternion rot)
    {
        if (prefab == null) return;

        Bullet companion = BulletPool.Instance != null
            ? BulletPool.Instance.Rent(prefab, pos, rot)
            : null;

        // Horizontal velocity along fire direction.
        float3 vel = (float3)(rot * Vector3.forward) * prefab.speed;

        // Artillery arc: override Y to sqrt(2)/2 * speed (matches original ArtilleryShell).
        if (prefab.AffectedByGravity)
            vel.y = MathFunctions.Sqrt2 / 2f * prefab.speed;

        PendingSpawns.Add(new SpawnRequest
        {
            Companion = companion,
            Position  = pos,
            Velocity  = vel,
            Data      = new BulletData
            {
                Damage           = prefab.damage,
                Penetration      = prefab.penetration,
                Lifetime         = prefab.lifetime,
                ColliderRadius   = prefab.ColliderRadius,
                ExplosionRadius  = prefab.ExplosionRadius,
                ExplosionDamage  = prefab.ExplosionDamage,
                AffectedByGravity = prefab.AffectedByGravity,
                CanRicochet       = prefab.CanRicochet,
            },
        });
    }
}
