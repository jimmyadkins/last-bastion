using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Object pool for swarmer death particle effects.
/// Eliminates Instantiate/Destroy GC spikes when many swarmers die per frame.
///
/// Place this component on a persistent GameObject in the scene.
/// SwarmerController.Die() calls SwarmerDeathVFXPool.Instance.Play() instead of
/// Instantiating directly.
/// </summary>
public class SwarmerDeathVFXPool : MonoBehaviour
{
    public static SwarmerDeathVFXPool Instance { get; private set; }

    [Tooltip("Death particle prefab — same one that was on SwarmerController.DeathVfxPrefab.")]
    [SerializeField] private GameObject m_prefab;

    [Tooltip("Number of pooled instances created at startup. Grows automatically if exhausted.")]
    [SerializeField] private int m_initialSize = 64;

    private readonly Queue<ParticleSystem> m_available = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < m_initialSize; i++)
            AllocateOne();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Play a death effect at <paramref name="position"/>.</summary>
    public void Play(Vector3 position)
    {
        if (m_prefab == null) return;

        var ps = m_available.Count > 0 ? m_available.Dequeue() : AllocateOne();

        ps.transform.position = position;
        ps.gameObject.SetActive(true);
        ps.Play();

        // Return to pool after the effect finishes.
        float duration = ps.main.duration + ps.main.startLifetime.constantMax;
        StartCoroutine(ReturnAfter(ps, Mathf.Max(duration, 0.1f)));
    }

    private ParticleSystem AllocateOne()
    {
        var go = Instantiate(m_prefab, transform);
        go.SetActive(false);
        var ps = go.GetComponent<ParticleSystem>();
        m_available.Enqueue(ps);
        return ps;
    }

    private IEnumerator ReturnAfter(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.SetActive(false);
        m_available.Enqueue(ps);
    }
}
