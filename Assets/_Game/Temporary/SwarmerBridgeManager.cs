using Latios;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Exposes the same spatial-query API that SwarmerManager previously served
/// directly, but reads live data from the ECS blackboard entity.
///
/// SwarmerManager delegates GetCellHeading / GetCoord / GetCellPosition calls
/// here when the ECS world is available.
/// </summary>
public class SwarmerBridgeManager : MonoBehaviour
{
    public static SwarmerBridgeManager Instance { get; private set; }

    private LatiosWorldUnmanaged m_latiosWorld;
    private bool                 m_valid;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // DefaultGameObjectInjectionWorld is set up by LatiosBootstrap before Start()
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            m_latiosWorld = world.Unmanaged.GetLatiosWorldUnmanaged();
            m_valid       = m_latiosWorld.isValid;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Cell-heading query ─────────────────────────────────────────────────

    /// <summary>Returns the normalised average heading for the cell at <paramref name="worldPos"/>.</summary>
    public Vector2 GetCellHeading(Vector3 worldPos)
    {
        return GetCellHeading(GetCoord(worldPos));
    }

    /// <summary>Returns the normalised average heading for the given cell coordinate.</summary>
    public Vector2 GetCellHeading(Vector2Int coord)
    {
        if (!TryGetGridData(out SwarmerGridData data)) return Vector2.zero;

        var key = new int2(coord.x, coord.y);
        if (data.CellHeadings.TryGetValue(key, out float2 h))
            return new Vector2(h.x, h.y);

        return Vector2.zero;
    }

    // ── Coordinate utilities ───────────────────────────────────────────────

    public Vector2Int GetCoord(Vector3 worldPos)
    {
        float cellSize = GetCellSize();
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.z / cellSize));
    }

    public Vector3 GetCellPosition(Vector2Int coord)
    {
        float cellSize     = GetCellSize();
        float halfCellSize = cellSize * 0.5f;
        return new Vector3(coord.x * cellSize + halfCellSize, 0f, coord.y * cellSize + halfCellSize);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private bool TryGetGridData(out SwarmerGridData data)
    {
        data = default;
        if (!m_valid) return false;

        var wbb = m_latiosWorld.worldBlackboardEntity;
        if (!wbb.HasCollectionComponent<SwarmerGridData>()) return false;

        data = wbb.GetCollectionComponent<SwarmerGridData>(readOnly: true);
        // Required when accessing collection components outside a tracked system
        wbb.UpdateMainThreadAccess<SwarmerGridData>(wasReadOnly: true);
        return true;
    }

    private float GetCellSize()
    {
        if (!m_valid) return Defines.EnemyGridCellSize;

        var wbb = m_latiosWorld.worldBlackboardEntity;
        if (wbb.HasComponent<SwarmerConfig>())
            return wbb.GetComponentData<SwarmerConfig>().CellSize;

        return Defines.EnemyGridCellSize;
    }
}
