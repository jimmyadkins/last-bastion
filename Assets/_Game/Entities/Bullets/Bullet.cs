using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Phase B: Visual-only companion MonoBehaviour.
/// ECS systems (BulletMoveSystem, BulletWallHitSystem, BulletSwarmerHitSystem,
/// BulletDeathSystem) own all logic. This GO holds MeshRenderer + TrailRenderer
/// and triggers death VFX/audio when BulletDeathSystem calls TriggerDeathFX().
///
/// TODO (editor): Remove Rigidbody and Collider from all bullet prefabs — ECS
/// drives movement and hit detection. Until removed, Rigidbody is forced kinematic.
/// </summary>
public class Bullet : MonoBehaviour
{
    public int   penetration = 0;
    public float speed       = 100f;
    public float damage      = 1f;
    public float lifetime    = 5f;
    public float ExplosionRadius = 0f;
    public float ExplosionDamage = 0f;

    [Tooltip("True for ArtyBullet — ECS BulletMoveSystem applies gravity to velocity.")]
    public bool  AffectedByGravity = false;
    [Tooltip("Sphere radius used by BulletSwarmerHitSystem for hit detection.")]
    public float ColliderRadius    = 0.2f;

    public VisualEffect explosionEffectPrefab;
    public AudioClip    explosionSound;

    // Set by BulletPool.CreateInstance so this instance knows which queue to return to.
    [HideInInspector] public Bullet PoolPrefab;

    private TrailRenderer m_trail;

    protected virtual void Awake()
    {
        m_trail = GetComponentInChildren<TrailRenderer>();
    }

    private void OnDisable()
    {
        m_trail?.Clear();
    }

    /// <summary>
    /// Called by BulletDeathSystem before returning this instance to the pool.
    /// Spawns explosion VFX and plays audio at the given world position.
    /// </summary>
    public void TriggerDeathFX(Vector3 worldPos)
    {
        if (explosionEffectPrefab != null)
        {
            VisualEffect explosion = Instantiate(explosionEffectPrefab, worldPos, Quaternion.identity);
            explosion.Play();
            Destroy(explosion.gameObject, explosion.GetFloat("Duration"));
        }

        if (explosionSound != null)
            AudioManager.Instance.PlayEffectAtPosition(explosionSound, worldPos);
    }
}
