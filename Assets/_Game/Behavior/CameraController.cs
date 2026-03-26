using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public float rotationSpeed = 100f; // Speed for keyboard rotation
    public float verticalSpeed = 100f;

    public Vector3 minBorder, maxBorder; // Bounds for camera movement
    public float zoomSpeed = 5f; // Speed for zooming
    public float normalSpeed = 5f;
    public float fastestSpeed = 15f;
    public float panSmoothSpeed = 10f; // Speed for smoothing the panning movement
    public float mouseMovementThreshold = 0.01f; // Threshold for detecting significant mouse movement
    public float maxPanDistance = 10f; // Maximum distance for a single pan update

    public float arrowRotationSpeed = 100f;

    public float horizontalSensitivity = 1f; // Sensitivity for horizontal mouse movement
    public float verticalSensitivity = 1f; // Sensitivity for vertical mouse movement

    private float speed;
    private Vector3 startPos;
    private Vector3 currentPos;
    private Vector3 targetPos; // Target position for panning and movement
    private Camera cam;
    private Transform childCam;
    private Plane plane;

    private void Awake()
    {
        cam = Camera.main;
        speed = normalSpeed;
        childCam = transform.GetChild(0).transform;
        plane = new Plane(Vector3.up, Vector3.zero);
        targetPos = transform.position; // Initialize target position
    }

    private void Update()
    {
        CameraRotate();
        CameraMove();
    }

    private void CameraMove()
    {
        // The reason the scroll sensitivity is so high is that my mousewheel only registers scrolling
        // for a few frames, which means it must travel very fast in those few frames. This may break on
        // your machine so change it if that's the case
        float scrollInput = Mouse.current.scroll.ReadValue().y;
        if (scrollInput != 0)
        {
            Vector3 zoomDirection = scrollInput * zoomSpeed * childCam.forward;
            targetPos += zoomDirection * Time.deltaTime;
        }

        // Keyboard movement
        var kb = Keyboard.current;
        if (kb.leftShiftKey.isPressed) targetPos += verticalSpeed * Time.deltaTime * transform.up;
        if (kb.leftCtrlKey.isPressed) targetPos -= verticalSpeed * Time.deltaTime * transform.up;
        if (kb.wKey.isPressed) targetPos += speed * Time.deltaTime * transform.forward;
        if (kb.sKey.isPressed) targetPos -= speed * Time.deltaTime * transform.forward;
        if (kb.aKey.isPressed) targetPos -= speed * Time.deltaTime * transform.right;
        if (kb.dKey.isPressed) targetPos += speed * Time.deltaTime * transform.right;


        // Movement with slide (middle mouse panning)
        var mouse = Mouse.current;
        if (mouse.middleButton.wasPressedThisFrame)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (plane.Raycast(ray, out float entry))
            {
                startPos = ray.GetPoint(entry);
            }
        }

        if (mouse.middleButton.isPressed)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (plane.Raycast(ray, out float entry))
            {
                currentPos = ray.GetPoint(entry);
                Vector3 movementDelta = startPos - currentPos;

                // Clamp the movement delta to the maximum allowed distance
                movementDelta = Vector3.ClampMagnitude(movementDelta, maxPanDistance);

                // Only update targetPos if the movement is significant
                if (movementDelta.magnitude > mouseMovementThreshold)
                {
                    targetPos += movementDelta;
                    startPos = currentPos; // Update startPos to currentPos to reset for next movement
                }
            }
        }

        // Apply bounds to keep the camera within the designated area
        targetPos.x = Mathf.Clamp(targetPos.x, minBorder.x, maxBorder.x);
        targetPos.y = Mathf.Clamp(targetPos.y, minBorder.y, maxBorder.y);
        targetPos.z = Mathf.Clamp(targetPos.z, minBorder.z, maxBorder.z);

        // Smoothly move the camera to the target position
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * panSmoothSpeed);
    }

    private void CameraRotate()
    {
        // Camera horizontal rotation with right mouse button
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse.rightButton.isPressed) // Right mouse button held down
        {
            Vector2 mouseDelta = mouse.delta.ReadValue();
            float mouseX = mouseDelta.x * horizontalSensitivity;
            float mouseY = mouseDelta.y * verticalSensitivity;
            RotateX(mouseY * rotationSpeed);
            RotateY(mouseX * rotationSpeed);
        }

        if (kb.upArrowKey.isPressed)
        {
            RotateX(arrowRotationSpeed);
        }

        if (kb.downArrowKey.isPressed)
        {
            RotateX(-arrowRotationSpeed);
        }

        // Rotate with Q and E keys
        if (kb.qKey.isPressed || kb.leftArrowKey.isPressed)
        {
            RotateY(-arrowRotationSpeed);
        }
        if (kb.eKey.isPressed || kb.rightArrowKey.isPressed)
        {
            RotateY(arrowRotationSpeed);
        }
    }

    private void RotateX(float angle)
    {
        Vector3 rot = childCam.localEulerAngles;
        rot.x = Mathf.Clamp(rot.x - angle * Time.deltaTime, 10f, 80f);
        childCam.localEulerAngles = rot;
    }

    private void RotateY(float angle)
    {
        Quaternion deltaRotation = Quaternion.AngleAxis(angle * Time.deltaTime, Vector3.up);
        transform.rotation = deltaRotation * transform.rotation;
    }
}
