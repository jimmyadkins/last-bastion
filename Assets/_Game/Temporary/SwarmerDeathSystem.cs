using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Main-thread managed system.
/// Companion GOs are destroyed by SwarmerController.Die() / SwarmerManager
/// (which calls Destroy(swarmer.gameObject)). When that happens, the companion
/// MB reference becomes null via Unity's == operator on UnityEngine.Object.
/// This system sweeps for such entities and destroys them via ECB so ECS
/// stays consistent with the GameObject world.
///
/// [DisableAutoCreation] — managed by SwarmerSuperSystem only.
/// </summary>
[DisableAutoCreation]
public partial class SwarmerDeathSystem : SystemBase
{
    private EntityQuery m_query;

    protected override void OnCreate()
    {
        m_query = GetEntityQuery(
            ComponentType.ReadOnly<SwarmerCompanionRef>(),
            ComponentType.ReadOnly<SwarmerHealth>());
    }

    protected override void OnUpdate()
    {
        var ecb      = new EntityCommandBuffer(Allocator.Temp);
        var entities = m_query.ToEntityArray(Allocator.Temp);
        var healths  = m_query.ToComponentDataArray<SwarmerHealth>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var companion = EntityManager.GetComponentObject<SwarmerCompanionRef>(entities[i]);
            // Unity's == null check on a UnityEngine.Object returns true if the GO was destroyed
            if (companion == null || companion.MB == null)
            {
                ecb.DestroyEntity(entities[i]);
                continue;
            }

            if (healths[i].Current <= 0f)
            {
                companion.MB.Die(); // spawns VFX, calls Destroy(gameObject)
                ecb.DestroyEntity(entities[i]);
            }
        }

        healths.Dispose();

        entities.Dispose();

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
