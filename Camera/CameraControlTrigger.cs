using UnityEngine;
using Unity.Cinemachine;

public class CameraControlTrigger : MonoBehaviour
{
    [Header("Camera Swap Settings")]
    public bool swapCameras = false;
    public CinemachineCamera cameraOnLeft;
    public CinemachineCamera cameraOnRight;
    public bool horizontal;

    [Header("Camera Pan Settings")]
    public bool panCameraOnContact = false;
    public PanDirection panDirection;
    public float panDistance = 3f;
    public float panTime = 0.35f;

    private Collider2D coll;

    void Start()
    {
        coll = GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            if (swapCameras && cameraOnLeft != null && cameraOnRight != null)
            {
                Vector2 exitDirection = (collision.transform.position - coll.bounds.center).normalized;
                Debug.Log("SWAPPED: " + cameraOnLeft + " | " + cameraOnRight);
                CameraManager.instance.SwapCamera(cameraOnLeft, cameraOnRight, exitDirection, horizontal);
            }

            if (panCameraOnContact)
            {
                CameraManager.instance.PanCameraOnContact(
                    panDistance,
                    panTime,
                    panDirection,
                    false
                );
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && panCameraOnContact)
        {
            CameraManager.instance.PanCameraOnContact(
                panDistance,
                panTime,
                panDirection,
                true
            );
        }
    }
}

public enum PanDirection
{
    Up,
    Down,
    Left,
    Right
}
