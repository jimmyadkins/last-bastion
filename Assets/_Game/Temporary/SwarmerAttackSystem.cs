using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Main-thread managed system.
/// For every swarmer that is currently attacking (SwarmerIsAttacking.Value == true),
/// calls IBuilding.TakeDamage via the companion MonoBehaviour's target reference.
/// The companion MB owns the IBuilding reference because it is a managed object.
///
/// [DisableAutoCreation] — managed by SwarmerSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class SwarmerAttackSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerIsAttacking>(),
            ComponentType.ReadOnly<SwarmerCompanionRef>());
        RequireForUpdate<SwarmerConfig>();
    }

    protected override void OnUpdate()
    {
        int attackDamage = SystemAPI.GetSingleton<SwarmerConfig>().AttackDamage;

        var entities   = m_query.ToEntityArray(Allocator.Temp);
        var attacking  = m_query.ToComponentDataArray<SwarmerIsAttacking>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            if (!attacking[i].Value) continue;

            var companion = EntityManager.GetComponentObject<SwarmerCompanionRef>(entities[i]);
            SwarmerController mb = companion?.MB;
            if (mb == null) continue;

            mb.Target?.TakeDamage(attackDamage);
        }

        entities.Dispose();
        attacking.Dispose();
    }
}
