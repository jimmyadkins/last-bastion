using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase A: Object pool for all bullet types.
/// Eliminates Instantiate/Destroy GC spikes at high fire rates.
///
/// Add this component to a persistent scene GameObject. Populate Entries in the
/// Inspector with each bullet prefab and a warm-up size. Turrets call
/// BulletPool.Instance.Rent() instead of Instantiate; Bullet.cs calls
/// Return() instead of Destroy().
/// </summary>
public class BulletPool : MonoBehaviour
{
    public static BulletPool Instance { get; private set; }

    [System.Serializable]
    public struct Entry
    {
        public Bullet   Prefab;
        public int      InitialSize;
    }

    [Tooltip("One entry per bullet prefab variant (Gatling, Cannon, Arty, Railgun, Base).")]
    public Entry[] Entries;

    private readonly Dictionary<Bullet, Queue<Bullet>> m_pools = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var entry in Entries)
        {
            if (entry.Prefab == null) continue;
            var q = new Queue<Bullet>(entry.InitialSize);
            for (int i = 0; i < entry.InitialSize; i++)
                q.Enqueue(CreateInstance(entry.Prefab));
            m_pools[entry.Prefab] = q;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Returns an active bullet positioned at <paramref name="pos"/> facing <paramref name="rot"/>.
    /// Grows the pool automatically if exhausted.
    /// </summary>
    public Bullet Rent(Bullet prefab, Vector3 pos, Quaternion rot)
    {
        if (!m_pools.TryGetValue(prefab, out var q))
            m_pools[prefab] = q = new Queue<Bullet>();

        Bullet b = q.Count > 0 ? q.Dequeue() : CreateInstance(prefab);
        b.transform.SetPositionAndRotation(pos, rot);
        b.gameObject.SetActive(true);
        return b;
    }

    /// <summary>Called by Bullet when it expires or is destroyed.</summary>
    public void Return(Bullet prefab, Bullet instance)
    {
        instance.gameObject.SetActive(false);
        if (!m_pools.TryGetValue(prefab, out var q))
            m_pools[prefab] = q = new Queue<Bullet>();
        q.Enqueue(instance);
    }

    private Bullet CreateInstance(Bullet prefab)
    {
        Bullet b = Instantiate(prefab, transform);
        b.PoolPrefab = prefab;
        b.gameObject.SetActive(false);
        return b;
    }
}
