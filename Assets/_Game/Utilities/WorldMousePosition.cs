using UnityEngine;
using UnityEngine.InputSystem;

public class WorldMousePosition : MonoBehaviour
{
    public static WorldMousePosition Instance { get; private set; }

    private Vector3 m_position = Vector3.zero;
    public Vector3 Position => m_position;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        Plane plane = new Plane(Vector3.up, Vector3.zero);

        if (plane.Raycast(ray, out float d))
        {
            m_position = ray.GetPoint(d);
        }
        else
        {
            m_position = Vector3.zero;
        }
    }
}
