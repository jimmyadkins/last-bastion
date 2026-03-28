using Unity.Entities;
using Unity.Mathematics;

// ── Kinematic state ──────────────────────────────────────────────────────────
public struct BulletPosition     : IComponentData { public float3 Value; }
public struct BulletVelocity     : IComponentData { public float3 Value; }
public struct BulletPrevPosition : IComponentData { public float3 Value; }  // written by MoveSystem for wall sweep

// ── Per-bullet config (data-driven, replaces Bullet class hierarchy) ─────────
public struct BulletData : IComponentData
{
    public float Damage;
    public int   Penetration;       // remaining; decremented on each swarmer hit
    public float Lifetime;          // remaining seconds
    public float ColliderRadius;    // sphere radius for swarmer distance check
    public float ExplosionRadius;   // 0 = no AoE
    public float ExplosionDamage;
    public bool  AffectedByGravity; // true for ArtyBullet
}

// ── Companion link (managed) ─────────────────────────────────────────────────
public class BulletCompanionRef : IComponentData { public Bullet MB; }

// ── Death tag (IEnableableComponent — disabled on spawn, enabled when dead) ──
public struct BulletDeadTag : IComponentData, IEnableableComponent { }
