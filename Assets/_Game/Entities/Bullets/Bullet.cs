using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

public class Bullet : MonoBehaviour
{
    public int penetration = 0;

    public float speed = 100f;
    public float damage = 1f;
    public float lifetime = 5f;
    public float ExplosionRadius = 0f;
    public float ExplosionDamage = 0f;

    public VisualEffect explosionEffectPrefab;
    public AudioClip explosionSound;

    protected Rigidbody rb;

    // Set by BulletPool when renting so the bullet can return itself.
    [HideInInspector] public Bullet PoolPrefab;

    private int   m_initialPenetration;
    private TrailRenderer m_trail;

    protected virtual void Awake()
    {
        rb                 = GetComponent<Rigidbody>();
        m_initialPenetration = penetration;
        m_trail            = GetComponentInChildren<TrailRenderer>();
    }

    // OnEnable fires on first activation AND every pool re-use, replacing Start().
    protected virtual void OnEnable()
    {
        StopAllCoroutines();
        penetration = m_initialPenetration;
        SetInitialVelocity();
        StartCoroutine(LifetimeRoutine());
    }

    protected virtual void SetInitialVelocity()
    {
        rb.linearVelocity = transform.forward * speed;
    }

    private void OnDisable()
    {
        rb.linearVelocity = Vector3.zero;
        m_trail?.Clear();
    }

    private IEnumerator LifetimeRoutine()
    {
        yield return new WaitForSeconds(lifetime);
        ReturnToPool();
    }

    // Call instead of Destroy(gameObject) everywhere.
    protected void ReturnToPool()
    {
        if (BulletPool.Instance != null && PoolPrefab != null)
            BulletPool.Instance.Return(PoolPrefab, this);
        else
            Destroy(gameObject);
    }

    // Swarmer SphereCollider is a trigger (no Rigidbody in Phase 4), so direct hits arrive here.
    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.TryGetComponent<SwarmerController>(out var enemy)) return;

        if (ExplosionRadius > 0)
        {
            foreach (var col in Physics.OverlapSphere(transform.position, ExplosionRadius, 1 << Defines.SwarmerLayer))
            {
                if (col.gameObject.TryGetComponent<SwarmerController>(out var splash))
                    splash.TakeDamage(ExplosionDamage);
            }
        }

        if (explosionEffectPrefab != null)
        {
            VisualEffect explosion = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            explosion.Play();
            Destroy(explosion.gameObject, explosion.GetFloat("Duration"));
        }

        enemy.TakeDamage(damage);

        if (--penetration <= 0)
        {
            if (explosionSound != null)
                AudioManager.Instance.PlayEffectAtPosition(explosionSound, transform.position);
            ReturnToPool();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (ExplosionRadius > 0)
        {
            foreach (var collider in Physics.OverlapSphere(transform.position, ExplosionRadius, 1 << Defines.SwarmerLayer))
            {
                if (collider.gameObject.TryGetComponent<SwarmerController>(out var splash))
                    splash.TakeDamage(ExplosionDamage);
            }
        }

        if (explosionEffectPrefab != null)
        {
            VisualEffect explosion = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            explosion.Play();
            Destroy(explosion.gameObject, explosion.GetFloat("Duration"));
        }

        if (collision.gameObject.TryGetComponent<SwarmerController>(out var enemy))
        {
            enemy.TakeDamage(damage);
        }
        else
        {
            Vector3 collisionNormal = collision.contacts[0].normal;
            float collisionAngle = Vector3.Angle(collisionNormal, transform.forward) - 90f;
            if (collisionAngle <= Defines.RicochetAngle)
                transform.forward = Vector3.Reflect(transform.forward, collisionNormal);
            else
                penetration = 0;
        }

        rb.linearVelocity = transform.forward * speed;

        if (--penetration <= 0)
        {
            if (explosionSound != null)
                AudioManager.Instance.PlayEffectAtPosition(explosionSound, transform.position);
            ReturnToPool();
        }
    }
}
