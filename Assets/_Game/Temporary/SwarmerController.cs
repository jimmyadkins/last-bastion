using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(Rigidbody))]
public class SwarmerController : MonoBehaviour
{
    // ── ECS companion entity ──────────────────────────────────────────────
    private Entity m_entity;
    private EntityManager m_em;
    private bool m_entityCreated;

    /// <summary>Exposes the Rigidbody to the ECS move-apply system.</summary>
    public Rigidbody GetRigidbody() => m_rb;

    [SerializeField] private float maxHealth = 5f;

    [SerializeField]
    private GameObject DeathVfxPrefab;
    //[SerializeField]
    //private GameObject AttackVfxPrefab;
    private VisualEffect attackVFX;

    [SerializeField] private AudioClip deathSound;
    private AudioSource m_attackAudioSource;

    private float currentHealth;

    public bool ShowDesiredHeading = false;
    public float separationWeight;
    public float obstacleWeight;

    private LayerMask m_environmentLayer;
    private LayerMask m_swarmerLayer;
    private LayerMask m_targetsLayer;
    private Rigidbody m_rb;

    private SwarmerManager m_manager;
    private float m_radius;
    public float Radius => m_radius;

    // Written each SyncToECS() by reading SwarmerSeparation from the companion ECS entity (Psyshock result)
    public Vector3 PrecomputedSeparation;

    // Written by SwarmerManager each FixedUpdate via RaycastCommand batch
    private RaycastHit m_preForwardHit;
    private RaycastHit m_preRightHit;
    private RaycastHit m_preLeftHit;
    private RaycastHit m_preLeftWhisker0;
    private RaycastHit m_preLeftWhisker1;
    private RaycastHit m_preRightWhisker0;
    private RaycastHit m_preRightWhisker1;

    public void SetPrecomputedRaycasts(
        RaycastHit fwd, RaycastHit right, RaycastHit left,
        RaycastHit leftW0, RaycastHit leftW1,
        RaycastHit rightW0, RaycastHit rightW1)
    {
        m_preForwardHit    = fwd;
        m_preRightHit      = right;
        m_preLeftHit       = left;
        m_preLeftWhisker0  = leftW0;
        m_preLeftWhisker1  = leftW1;
        m_preRightWhisker0 = rightW0;
        m_preRightWhisker1 = rightW1;
    }

    private IBuilding m_target;
    private int m_attention;

    private bool m_bAttacking = false;
    private bool m_bAttackingLastFrame = false;
    private bool m_bFacingTarget = false;

    public bool IsAttacking => m_bAttacking;
    public IBuilding Target => m_target;

    // 'EGrid' is enemy grid
    // this is to differentiate between the building grid and the enemy grid
    public Vector2Int EGridCoord { get; set; }

    protected void Awake()
    {
        m_environmentLayer = 1 << Defines.EnvironmentLayer;
        m_swarmerLayer     = 1 << Defines.SwarmerLayer;
        m_targetsLayer     = 1 << Defines.PlayerLayer;

        m_rb = GetComponent<Rigidbody>();
        m_radius = GetComponent<SphereCollider>().radius;

        SwarmerManager.Instance.Register(this);
        m_manager = SwarmerManager.Instance;

        currentHealth = maxHealth;

        attackVFX = GetComponentInChildren<VisualEffect>();
        if (attackVFX != null)
        {
            attackVFX.Stop();
        }

        m_attackAudioSource = GetComponent<AudioSource>();

        // ── Create companion ECS entity ──────────────────────────────────
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            m_em     = world.EntityManager;
            m_entity = m_em.CreateEntity();
            m_em.SetName(m_entity, gameObject.name);

            Vector3 initPos = transform.position;
            Vector3 initFwd = transform.forward;
            m_em.AddComponentData(m_entity, new SwarmerPosition   { Value   = new float3(initPos.x, initPos.y, initPos.z) });
            m_em.AddComponentData(m_entity, new SwarmerVelocity   { Value   = float3.zero });
            m_em.AddComponentData(m_entity, new SwarmerHeading    { Forward = new float3(initFwd.x, initFwd.y, initFwd.z) });
            m_em.AddComponentData(m_entity, new SwarmerRadius     { Value   = m_radius });
            m_em.AddComponentData(m_entity, new EnemyGridCell     ());
            m_em.AddComponentData(m_entity, new SwarmerAvoidanceInput ());
            m_em.AddComponentData(m_entity, new SwarmerSeparation ());
            m_em.AddComponentData(m_entity, new SwarmerTargetPos  ());
            m_em.AddComponentData(m_entity, new SwarmerDesiredVelocity ());
            m_em.AddComponentData(m_entity, new SwarmerIsAttacking ());
            m_em.AddComponentObject(m_entity, new SwarmerCompanionRef { MB = this });

            m_entityCreated = true;
        }
    }

    protected void Update()
    {
        if (!m_manager.DebugMovement)
        {
            return;
        }

        Debug.DrawRay(forwardRay.origin, forwardRay.direction * m_manager.ObstacleAvoidDistance, forwardHit ? Color.red : Color.white);
        Debug.DrawRay(leftRay.origin, leftRay.direction * m_manager.ObstacleAvoidDistance, leftHit ? Color.red : Color.white);
        Debug.DrawRay(rightRay.origin, rightRay.direction * m_manager.ObstacleAvoidDistance, rightHit ? Color.red : Color.white);

        if (!forwardHit && !leftHit && !rightHit)
        {
            return;
        }

        for (int i = 0; i < 2; ++i)
        {
            Debug.DrawRay(leftRays[i].origin, leftRays[i].direction * m_manager.WhiskerDistance, leftHits[i] ? Color.red : Color.white);
            Debug.DrawRay(rightRays[i].origin, rightRays[i].direction * m_manager.WhiskerDistance, rightHits[i] ? Color.red : Color.white);
        }
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // Perform any destruction logic, like playing an animation or sound
        //Debug.Log($"{gameObject.name} has been destroyed.");
        if (DeathVfxPrefab != null)
        {
            GameObject vfxInstance = Instantiate(DeathVfxPrefab, transform.position, Quaternion.identity);

            ParticleSystem particleSystem = vfxInstance.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                Destroy(vfxInstance, particleSystem.main.duration);
            }
            else
            {
                Destroy(vfxInstance, 8.0f);
            }
        }
        Destroy(gameObject); // Destroy the swarmer
    }

    // DEBUG 
    private Ray forwardRay;
    private bool forwardHit;
    private Ray leftRay;
    private bool leftHit;
    private Ray rightRay;
    private bool rightHit;

    private Ray[] leftRays = new Ray[2];
    private Ray[] rightRays = new Ray[2];

    bool[] leftHits = new bool[2];
    bool[] rightHits = new bool[2];


    public void UpdateSwarmer()
    {
        // Make sure to clear the reference if our target gets destroyed
        if (m_target != null && (m_target as Component) == null)
        {
            m_target = null;
        }

        Vector3 forward = transform.forward;

        Quaternion avoidRot = Quaternion.AngleAxis(m_manager.AvoidanceAngle, Vector3.up);

        forwardRay = new Ray(transform.position, forward);
        rightRay   = new Ray(transform.position + transform.right * m_radius, avoidRot * forward);
        leftRay    = new Ray(transform.position - transform.right * m_radius, Quaternion.Inverse(avoidRot) * forward);

        AvoidanceDistances d = new();

        RaycastHit forwardHitInfo = m_preForwardHit;
        RaycastHit rightHitInfo   = m_preRightHit;
        RaycastHit leftHitInfo    = m_preLeftHit;
        forwardHit = forwardHitInfo.collider != null;
        rightHit   = rightHitInfo.collider   != null;
        leftHit    = leftHitInfo.collider    != null;

        HandleTargetSelection(ref d, in forwardHitInfo, in rightHitInfo, in leftHitInfo);

        for (int i = 0; i < 2; ++i)
        {
            float dAngle = i * 45;
            const float initialAngle = 45;
            Quaternion rotation = Quaternion.AngleAxis(initialAngle + dAngle, Vector3.up);

            rightRays[i] = new Ray(transform.position, rotation * transform.forward);
            leftRays[i] = new Ray(transform.position, Quaternion.Inverse(rotation) * transform.forward);


            RaycastHit lInfo = i == 0 ? m_preLeftWhisker0  : m_preLeftWhisker1;
            RaycastHit rInfo = i == 0 ? m_preRightWhisker0 : m_preRightWhisker1;
            leftHits[i]  = lInfo.collider != null;
            rightHits[i] = rInfo.collider != null;

            d.LeftDistances  += lInfo.distance;
            d.RightDistances += rInfo.distance;
        }

        if (m_target != null)
        {
            if (m_attention == 0)
            {
                m_target = null;
            }
            else
            {
                --m_attention;
            }
        }

        SwarmerTarget st = SwarmerTarget.Instance;
        Vector2 targetPos =
            m_target != null ? m_target.GetPosition().xz() :
            st != null ? st.transform.position.xz() :
            transform.position.xz();
        Vector2 toTarget = (targetPos - transform.position.xz()).normalized;

        float cross = MathFunctions.Cross(forward.xz(), toTarget);
        int favoredRotation = -(int)Mathf.Sign(cross);
        int avoidanceRotation = 0;

        // If we need to avoid something
        if (forwardHit || rightHit || leftHit)
        {
            avoidanceRotation = GetAvoidance(favoredRotation, ref d);
        }

        // Separation vector is now precomputed by SwarmerManager via Burst SeparationJob
        Vector2 separation = PrecomputedSeparation.xz();
        //Debug.Log(separation);

        Vector2 alignment = m_manager.GetCellHeading(transform.position);

        //Vector2 desiredHeading = avoidanceRotation == 0 ? (toTarget + alignment).normalized : transform.right * avoidanceRotation;

        Vector2 desiredHeading = (toTarget * m_manager.TargetWeight +
                                  alignment * m_manager.AlignmentWeight +
                                  (transform.right * avoidanceRotation).xz() * d.AvoidanceStrength * m_manager.ObstacleWeight +
                                  separation * m_manager.SeparationWeight
                                  ).normalized;

        separationWeight = (separation * m_manager.SeparationWeight).magnitude;
        obstacleWeight = ((transform.right * avoidanceRotation).xz() * d.AvoidanceStrength * m_manager.ObstacleWeight).magnitude;


        //transform.Rotate(new Vector3(0, m_manager.TurnSpeed * (avoidanceRotation == 0 ? favoredRotation * Mathf.Min(Mathf.Abs(cross),1) : avoidanceRotation) * Time.fixedDeltaTime, 0));
        if (ShowDesiredHeading)
        {
            Debug.DrawLine(transform.position, transform.position + MathFunctions.UnflattenVector2(desiredHeading) * 5, Color.magenta);
            Debug.DrawLine(transform.position, transform.position + MathFunctions.UnflattenVector2(separation), Color.green);
        }
        var targetRotation = Quaternion.LookRotation(MathFunctions.UnflattenVector2(desiredHeading), Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, m_manager.TurnSpeed * Time.fixedDeltaTime);
        Move(d);

    }

    private void HandleTargetSelection(ref AvoidanceDistances d, in RaycastHit center, in RaycastHit right, in RaycastHit left)
    {
        m_bFacingTarget = false;
        if (center.collider?.gameObject.layer == Defines.PlayerLayer)
        {
            SetTarget(center);
            m_bFacingTarget = true;
            m_attention = Defines.SwarmerAttentionSpan;
        }
        // Because the right is checked first it means it will always favor a right target
        // maybe we want to randomize this?
        else if (m_target == null && right.collider?.gameObject.layer == Defines.PlayerLayer)
        {
            SetTarget(right);
            m_attention = Defines.SwarmerAttentionSpan;
        }
        else if (m_target == null && left.collider?.gameObject.layer == Defines.PlayerLayer)
        {
            SetTarget(left);
            m_attention = Defines.SwarmerAttentionSpan;
        }

        if (m_target is Wall)
        {
            // perform sanity check
            Vector2 t = (SwarmerTarget.Instance.transform.position.xz() - transform.position.xz()).normalized;
            Vector3 toTarget = new(t.x, 0, t.y);
            Vector3 pos = transform.position + toTarget * m_manager.ObstacleAvoidDistance/2;
            Quaternion atTarget = Quaternion.LookRotation(toTarget, Vector3.up);
            bool bBlocker = Physics.CheckBox(
                pos,
                new(m_radius, m_radius, m_manager.ObstacleAvoidDistance/2),
                atTarget,
                m_environmentLayer | m_targetsLayer);

            Vector3 f = atTarget * Vector3.forward;

            if (!bBlocker)
            {
                m_target = null;
            }
        }

        if (m_target == null)
        {
            d.CenterDistance = center.distance;
            d.RightDistance = right.distance;
            d.LeftDistance = left.distance;
        }
        else
        {
            d.CenterDistance = center.collider != null &&
                center.collider.gameObject.layer == Defines.EnvironmentLayer ?
                center.distance : 0;
            d.RightDistance = right.collider != null &&
                right.collider.gameObject.layer == Defines.EnvironmentLayer ?
                right.distance : 0;
            d.LeftDistance = left.collider != null &&
                left.collider.gameObject.layer == Defines.EnvironmentLayer ?
                left.distance : 0;
        }

        if (m_target != null)
        {
            Debug.DrawLine(transform.position, m_target.GetPosition(), Color.black);
        }
    }

    private void SetTarget(in RaycastHit hit)
    {
        Component component = hit.transform.parent.GetComponent(typeof(IBuilding));
        if (component is IBuilding building)
        {
            m_target = building;
        }
        else
        {
            Debug.LogError($"A building did not have a component implementing 'IBuilding' on it. There is no way to track its data");
        }
    }

    private int GetAvoidance(int favoredRotation, ref AvoidanceDistances dist)
    {
        dist.AvoidanceStrength =
            GetDistanceValue(dist.RightDistance) +
            GetDistanceValue(dist.CenterDistance) +
            GetDistanceValue(dist.LeftDistance);


        // If we just need to adjust a little bit
        if (!rightHit || !leftHit)
        {
            return rightHit ? -1 : 1;
        }

        // see if we've found some empty space
        for (int i = 0; i < 2; ++i)
        {
            if (!rightHits[i] || !leftHits[i])
            {
                if (!rightHits[i] && !leftHits[i])
                {
                    return favoredRotation;
                }
                else
                {
                    return rightHits[i] ? -1 : 1;
                }
            }
        }

        float rightBlockers = dist.RightDistance + dist.RightDistances;
        float leftBlockers = dist.LeftDistance + dist.LeftDistances;
        return rightBlockers < leftBlockers ? -1 : 1;
    }

    private float GetDistanceValue(float value)
    {
        float percentDistance = Mathf.Max(value - m_radius, 0) / (m_manager.ObstacleAvoidDistance - m_radius);
        return value == 0 ? value : Mathf.Lerp(1000, 1, percentDistance);
    }

    private void Move(in AvoidanceDistances d)
    {
        m_bAttacking = false; // reset
        Vector2 currentV = m_rb.linearVelocity.xz();

        SwarmerTarget st = SwarmerTarget.Instance;
        Vector2 targetPos =
            m_target != null ? m_target.GetPosition().xz() :
            st != null ? st.transform.position.xz() :
            transform.position.xz();
        float distToTarget = (targetPos - transform.position.xz()).magnitude;

        float speed = Mathf.Lerp(1, m_manager.MaxSpeed, Mathf.Clamp01(distToTarget / 5));
        Vector2 desiredV = transform.forward.xz() * speed;

        // Check if we have a valid target
        if (m_target != null)
        {
            Vector2 toTarget = m_target.GetPosition().xz() - transform.position.xz();
            float dist = toTarget.magnitude;
            // check if we are going in for an attack
            if (m_target is HQ)
            {
                // If the target is HQ, move directly toward it at max speed
                desiredV = (toTarget / dist) * m_manager.MaxSpeed;
            }
            else if (dist <= m_manager.AttackDistance * 1.5f)
            {
                m_bAttacking = m_bFacingTarget;
                if (m_bAttacking)
                {
                    if (attackVFX != null && !(attackVFX.aliveParticleCount > 0)) // VisualEffect-specific check
                    {
                        attackVFX.Play(); // Start the VFX if it's not already playing
                    }
                    if (!m_bAttackingLastFrame)
                    {
                        m_attackAudioSource.Play();
                    }
                    m_bAttackingLastFrame = true;
                }
                float value = Mathf.Clamp(0.5f * (dist - m_manager.AttackDistance), -1, 1);
                desiredV = value * m_manager.MaxSpeed * (toTarget / dist);

            }
            else
            {
                if (attackVFX != null && attackVFX.aliveParticleCount > 0)
                {
                    attackVFX.Stop(); // Stop the VFX when not attacking
                }
                m_attackAudioSource.Stop();
                m_bAttacking = false;
                m_bAttackingLastFrame = false;
            }
        }

        Vector2 dv = Vector2.ClampMagnitude(desiredV-currentV, m_manager.Acceleration);
        m_rb.AddForce(MathFunctions.UnflattenVector2(dv), ForceMode.Impulse);

        if (DeservesSlowdown(d.CenterDistance) ||
            DeservesSlowdown(d.RightDistance) ||
            DeservesSlowdown(d.LeftDistance))
        {
            m_rb.linearVelocity *= .1f;
        };
    }

    private bool DeservesSlowdown(float distance)
    {
        return distance != 0 && distance < m_manager.ObstacleAvoidSlowdownRange;
    }

    protected void OnDestroy()
    {
        m_manager.Deregister(this);

        // Destroy companion entity if it still exists
        if (m_entityCreated)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && m_em.Exists(m_entity))
            {
                m_em.DestroyEntity(m_entity);
            }
            m_entityCreated = false;
        }
    }

    /// <summary>
    /// Called by SwarmerManager each FixedUpdate before UpdateSwarmer().
    /// Writes the latest MB state into the companion ECS components so the
    /// Burst steering systems see fresh data.
    /// </summary>
    public void SyncToECS()
    {
        if (!m_entityCreated) return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || !m_em.Exists(m_entity)) return;

        Vector3 pos3 = transform.position;
        Vector3 fwd3 = transform.forward;
        m_em.SetComponentData(m_entity, new SwarmerPosition { Value   = new float3(pos3.x, pos3.y, pos3.z) });
        m_em.SetComponentData(m_entity, new SwarmerHeading  { Forward = new float3(fwd3.x, fwd3.y, fwd3.z) });
        Vector3 vel = m_rb.linearVelocity;
        m_em.SetComponentData(m_entity, new SwarmerVelocity { Value = new float3(vel.x, vel.y, vel.z) });
        // SwarmerSeparation is written by SwarmerSeparationSystem (Psyshock FindPairs) — not synced from MB.

        SwarmerTarget st = SwarmerTarget.Instance;
        Vector3 rawTargetPos =
            m_target != null ? m_target.GetPosition() :
            st != null       ? st.transform.position :
            transform.position;
        float3 targetPos = new float3(rawTargetPos.x, rawTargetPos.y, rawTargetPos.z);

        m_em.SetComponentData(m_entity, new SwarmerTargetPos
        {
            Value        = targetPos,
            HasTarget    = m_target != null,
            IsFacingTarget = m_bFacingTarget,
            IsHQTarget   = m_target is HQ,
        });

        m_em.SetComponentData(m_entity, new SwarmerIsAttacking { Value = m_bAttacking });

        // Read Psyshock separation back from ECS → MB uses it in UpdateSwarmer steering
        var ecsSep = m_em.GetComponentData<SwarmerSeparation>(m_entity);
        PrecomputedSeparation = new Vector3(ecsSep.Value.x, ecsSep.Value.y, ecsSep.Value.z);

    }

    private bool m_bMarked = false;
    public bool IsMarked => m_bMarked;

    public void MarkTarget()
    {
        m_bMarked = true;
    }

    public void UnmarkTarget()
    {
        m_bMarked = false;
    }

    private struct AvoidanceDistances
    {
        public float CenterDistance;
        public float RightDistance;
        public float RightDistances;
        public float LeftDistance;
        public float LeftDistances;

        public float AvoidanceStrength;
    }
}
