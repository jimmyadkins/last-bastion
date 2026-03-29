using UnityEngine;

public class ArtyController : TurretController
{
    // ── Position-based targeting (set by TurretTargetingSystem) ──────────────
    private bool    m_hasAimPosition;
    private Vector3 m_aimPosition;

    /// <summary>
    /// Called by TurretManager.ApplyECSAssignments() with a pre-predicted world
    /// position: cluster center + (cell heading × maxSpeed × flight time).
    /// Supersedes the swarmer-tracking path for this shot.
    /// </summary>
    public void AssignTarget(Vector3 worldPos)
    {
        m_aimPosition    = worldPos;
        m_hasAimPosition = true;
    }

    // ─────────────────────────────────────────────────────────────────────────

    protected override bool RotateToFaceTarget()
    {
        Vector3 targetPosition;

        if (m_hasAimPosition)
        {
            // ECS already predicted the lead position — aim there directly.
            targetPosition = m_aimPosition;
        }
        else
        {
            // Fallback: swarmer-tracking path with internal velocity prediction.
            Vector3 targetVelocity = m_target.target != null ? m_target.target.LastKnownVelocity : Vector3.zero;
            float g             = Mathf.Abs(Physics.gravity.y);
            float timeToImpact  = MathFunctions.Sqrt2 * bulletSpeed / g;
            targetPosition      = m_target.lastPosition + targetVelocity * timeToImpact;
        }

        // Horizontal rotation (Y-axis)
        Vector3 horizontalDirection = new Vector3(targetPosition.x - transform.position.x, 0f, targetPosition.z - transform.position.z).normalized;
        Quaternion horizontalLookRotation = Quaternion.LookRotation(horizontalDirection);
        turretBase.rotation = Quaternion.RotateTowards(turretBase.rotation, horizontalLookRotation, Time.fixedDeltaTime * rotationSpeed);

        float angleFromTarget = Quaternion.Angle(turretBase.rotation, horizontalLookRotation);

        Vector2 toAimPoint = targetPosition.xz() - transform.position.xz();
        float distanceToAimPoint = toAimPoint.magnitude;
        toAimPoint /= distanceToAimPoint;

        float g2 = Mathf.Abs(Physics.gravity.y);
        float timeToImpact2 = MathFunctions.Sqrt2 * bulletSpeed / g2;
        float horizontalVelocityProportion = distanceToAimPoint / (bulletSpeed * timeToImpact2);
        if (horizontalVelocityProportion > 1)
        {
            // Target out of range for this arc — can't find a valid elevation.
            return false;
        }

        float elevationAngle = Mathf.Acos(horizontalVelocityProportion);
        elevationAngle *= Mathf.Rad2Deg;
        Quaternion elevationRotation = Quaternion.Euler(-elevationAngle, 0f, 0f);
        gunPivot.localRotation = Quaternion.RotateTowards(gunPivot.localRotation, elevationRotation, Time.fixedDeltaTime * rotationSpeed);

        float verticalAngleFromTarget = Quaternion.Angle(gunPivot.localRotation, elevationRotation);

        return Mathf.Max(angleFromTarget, verticalAngleFromTarget) <= Defines.TurretAimTolerance;
    }

    // targets will be found by TurretTargetingSystem via TurretManager
    protected override void FindTarget() { }

    protected override bool IsTargetValid()
    {
        if (m_hasAimPosition) return true;

        if (m_target.IsValid && m_target.IsAlive())
        {
            m_target.UpdateTarget();
            if ((m_target.lastPosition - transform.position).sqrMagnitude <= detectionRange * detectionRange)
            {
                return true;
            }
        }
        m_target.Invalidate();
        return false;
    }

    public override void UpdateTurret()
    {
        if (IsPreview)
        {
            return;
        }

        fireCooldown -= Time.fixedDeltaTime;

        if (!HasTarget && !FindCloseTarget())
        {
            FindTarget();
        }

        if (m_target.IsValid || m_hasAimPosition)
        {
            bool bIsAimed = RotateToFaceTarget();
            if (fireCooldown <= 0f && bIsAimed)
            {
                Fire();
                m_target.Invalidate();
                m_hasAimPosition = false; // clear after firing; get a new assignment next cycle
                fireCooldown = 1f / fireRate;
            }
        }
        else
        {
            OnStopFiring();
        }
    }
}
