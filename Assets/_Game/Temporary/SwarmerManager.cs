using System.Collections.Generic;
using System.Linq;
using Latios;
using Unity.Entities;
using UnityEngine;

public class SwarmerManager : MonoBehaviour
{
    public static SwarmerManager Instance { get; private set; }

    [Header("Movement")]
    public float MaxSpeed;
    public float Acceleration;
    public float LinearDamping;
    public float TurnSpeed;
    public float AvoidanceAngle;
    public float ObstacleAvoidDistance;
    public float WhiskerDistance;
    public float NeighborDetectionDistance;
    public float ObstacleAvoidSlowdownRange;
    public float TargetWeight;
    public float AlignmentWeight;
    public float ObstacleWeight;
    public float SeparationWeight;
    public bool DebugMovement = true;

    public float targetDestroyDistance;

    [Header("Attacking")]
    public float AttackDistance;
    public int AttackDamage;
    public int CollisionDamage;

    [Header("Grid Cells")]
    public float CellSize;
    public int DrawDistance;
    public bool Draw;

    [Header("Prefab")]
    public SwarmerController Prefab;

    private List<SwarmerController> m_swarmers = new();
    private Dictionary<Vector2Int, Vector2> m_averageHeading = new();
    private Dictionary<Vector2Int, List<SwarmerController>> m_enemiesByCell = new();

    public int SwarmerCount => m_swarmers.Count;

    protected void Awake()
    {
        Instance = this;
    }

    public void Start()
    {
        //SpawnSwarmers(Vector2.zero, 100);
        PushConfigToBlackboard();
    }

    /// <summary>
    /// Writes inspector-configured movement parameters to the ECS world
    /// blackboard entity so Burst systems can read them without managed calls.
    /// </summary>
    private void PushConfigToBlackboard()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var latiosWorld = world.Unmanaged.GetLatiosWorldUnmanaged();
        if (!latiosWorld.isValid) return;

        latiosWorld.worldBlackboardEntity.AddComponentData(new SwarmerConfig
        {
            MaxSpeed                 = MaxSpeed,
            Acceleration             = Acceleration,
            LinearDamping            = LinearDamping,
            TurnSpeed                = TurnSpeed,
            TargetWeight             = TargetWeight,
            AlignmentWeight          = AlignmentWeight,
            SeparationWeight         = SeparationWeight,
            ObstacleWeight           = ObstacleWeight,
            NeighborDetectionDistance = NeighborDetectionDistance,
            AttackDistance           = AttackDistance,
            CellSize                 = CellSize,
            TargetDestroyDistance    = targetDestroyDistance,
            AttackDamage             = AttackDamage,
            CollisionDamage          = CollisionDamage,
        });
    }

    public void Update()
    {
        if (!Draw)
        {
            return;
        }

        Vector2 bounds = new Vector2(-DrawDistance*CellSize, DrawDistance*CellSize);

        // Horizontal lines
        for (int i = -DrawDistance; i <= DrawDistance; ++i)
        {
            float pos = CellSize * i;
            Debug.DrawLine(new Vector3(bounds.x, 1, pos), new Vector3(bounds.y, 1, pos));
        }

        // Vertical lines
        for (int i = -DrawDistance; i <= DrawDistance; ++i)
        {
            float pos = CellSize*i;
            Debug.DrawLine(new Vector3(pos, 1, bounds.x), new Vector3(pos, 1, bounds.y));
        }
    }

    public void Register(SwarmerController swarmer)
    {
        m_swarmers.Add(swarmer);
        AssignSwarmerToGrid(swarmer);
    }

    // Phase 5: FixedUpdate logic moved to ECS systems in SwarmerSuperSystem:
    //   raycast batch + SyncToECS + UpdateSwarmer → SwarmerRaycastAndUpdateSystem
    //   attack damage                             → SwarmerAttackSystem
    //   HQ collision damage + destroy             → SwarmerHQDamageSystem
    //   BuildingManager.CleanupBuildings()        → BuildingManager.Update()

    public Vector2 GetCellHeading(Vector2Int coord)
    {
        // Prefer ECS bridge data if available
        if (SwarmerBridgeManager.Instance != null)
            return SwarmerBridgeManager.Instance.GetCellHeading(coord);

        if (m_averageHeading.TryGetValue(coord, out Vector2 h))
            return h;
        return Vector2.zero;
    }

    public Vector2 GetCellHeading(Vector3 pos)
    {
        if (SwarmerBridgeManager.Instance != null)
            return SwarmerBridgeManager.Instance.GetCellHeading(pos);

        Vector2Int coord = GetCoord(pos);
        if (m_averageHeading.TryGetValue(coord, out Vector2 h))
            return h;
        return Vector2.zero;
    }

    public Vector2Int GetCoord(Vector3 pos)
    {
        if (SwarmerBridgeManager.Instance != null)
            return SwarmerBridgeManager.Instance.GetCoord(pos);

        return new(Mathf.FloorToInt(pos.x / CellSize), Mathf.FloorToInt(pos.z / CellSize));
    }

    public Vector3 GetCellPosition(Vector2Int coord)
    {
        if (SwarmerBridgeManager.Instance != null)
            return SwarmerBridgeManager.Instance.GetCellPosition(coord);

        float halfCellSize = CellSize * 0.5f;
        return new Vector3((coord.x * CellSize) + halfCellSize, 0, (coord.y * CellSize) + halfCellSize);
    }


    /// <summary>
    /// Spawns swarmers in a tightly packed square
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="count"></param>
    public void SpawnSwarmers(Vector2 pos, int count)
    {
        const float radius = 0.5f; //hardcoding because this is temporary
        const float spacing = radius * 2;

        Instantiate(Prefab, new Vector3(pos.x, 0.5f, pos.y), Quaternion.identity);

        for (int i = 0, index = 1; index < count; ++i)
        {
            int steps = (i / 2) + 1;
            bool bVertical = i % 2 == 0;
            int sign = steps % 2 == 0 ? -1 : 1;

            Vector2 offset = new Vector2(
                bVertical ? 0 : sign,
                !bVertical ? 0 : sign);

            for (int j = 0; index < count && j < steps; ++j)
            {
                pos += offset * spacing;
                Instantiate(Prefab, new Vector3(pos.x, 0.5f, pos.y), Quaternion.identity);
                ++index;
            }
        }
    }

    public void AssignSwarmerToGrid(SwarmerController swarmer)
    {
        Vector2Int coord = GetCoord(swarmer.transform.position);
        swarmer.EGridCoord = coord;

        if (!m_enemiesByCell.ContainsKey(coord))
        {
            m_enemiesByCell[coord] = new();
        }

        m_enemiesByCell[coord].Add(swarmer);
    }

    public void UpdateSwarmerGridPosition(SwarmerController swarmer)
    {
        Vector2Int currentCoord = GetCoord(swarmer.transform.position);
        if (currentCoord == swarmer.EGridCoord)
        {
            return;
        }

        RemoveSwarmerFromGrid(swarmer);
        AssignSwarmerToGrid(swarmer);
    }
    private void RemoveSwarmerFromGrid(SwarmerController swarmer)
    {
        Vector2Int coord = swarmer.EGridCoord;
        m_enemiesByCell[coord].Remove(swarmer);
        if (m_enemiesByCell[coord].Count == 0)
        {
            m_enemiesByCell.Remove(coord); // Remove the cell entry if empty
        }
    }

    private static readonly List<SwarmerController> s_emptyCell = new();
    public List<SwarmerController> GetEnemiesInCell(Vector2Int coord)
    {
        if (m_enemiesByCell.ContainsKey(coord))
        {
            return m_enemiesByCell[coord];
        }
        return s_emptyCell; // Return an empty list if no enemies are in the cell
    }

    public List<List<SwarmerController>> GetMostPopulousCells()
    {
        const int maxCount = 10;

        List<(Vector2Int, int)> ls = new();
        foreach (var (coords, list) in m_enemiesByCell)
        {
            ls.Add((coords, list.Count));
            for (int i = 0; i < ls.Count-1; ++i)
            {
                if (ls[i].Item2 < ls[i+1].Item2)
                {
                    (ls[i], ls[i+1]) = (ls[i+1], ls[i]);
                }
            }
            if (ls.Count > maxCount)
            {
                ls.RemoveAt(ls.Count-1);
            }
        }

        List<List<SwarmerController>> result = new();
        foreach (var (coord, count) in ls)
        {
            if (count > 0)
            {
                result.Add(m_enemiesByCell[coord]);
            }
        }

        return result;
    }

    public void Reset()
    {
        var arrayCopy = m_swarmers.ToArray();
        foreach (var swarmer in arrayCopy)
        {
            Destroy(swarmer.gameObject);
        }
    }

    public void Deregister(SwarmerController swarmer)
    {
        m_swarmers.Remove(swarmer);
        RemoveSwarmerFromGrid(swarmer); // Remove swarmer from the dictionary
    }
}



