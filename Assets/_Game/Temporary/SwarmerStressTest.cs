using UnityEngine;

/// <summary>
/// Drop this on any GameObject in the test scene.
/// Hit Play — swarmers spawn immediately and head for SwarmerTarget.
///
/// Profiling tips:
///   Window → Analysis → Profiler (Deep Profile OFF for accurate timings)
///   Window → Analysis → Frame Debugger
///   Burst Inspector: Jobs → Burst → Open Inspector
/// </summary>
public class SwarmerStressTest : MonoBehaviour
{
    [Header("Spawn")]
    [Tooltip("Total swarmers to spawn at start.")]
    public int Count = 500;

    [Tooltip("World position to spawn the blob around.")]
    public Vector2 SpawnCenter = new Vector2(-30f, 0f);

    [Header("Display")]
    [Tooltip("Show live swarmer count in top-left corner.")]
    public bool ShowGUI = true;

    private void Start()
    {
        var mgr = SwarmerManager.Instance;
        if (mgr == null)
        {
            Debug.LogError("[SwarmerStressTest] No SwarmerManager found in scene.");
            return;
        }
        mgr.SpawnSwarmers(SpawnCenter, Count);
    }

    private void OnGUI()
    {
        if (!ShowGUI) return;
        var mgr = SwarmerManager.Instance;
        if (mgr == null) return;
        GUI.Label(new Rect(10, 10, 300, 25),
            $"Swarmers: {mgr.SwarmerCount}  |  FPS: {1f / Time.smoothDeltaTime:F0}");
    }
}
