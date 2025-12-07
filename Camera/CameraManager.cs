using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class CameraManager : MonoBehaviour
{
    public static CameraManager instance;

    [Header("Cameras")]
    [SerializeField] private CinemachineCamera[] allVirtualCameras;

    private CinemachineCamera currentCamera;
    private CinemachineCameraOffset cameraOffset;
    private CinemachinePositionComposer framingTransposer;
    private Coroutine panCameraCoroutine;
    private Vector2 startingTrackedObjectOffset;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            InitializeCamera();
        }
        else
        {
            Destroy(gameObject);
        }

        startingTrackedObjectOffset = cameraOffset.Offset;
    }

    private void InitializeCamera()
    {
        foreach (var cam in allVirtualCameras)
        {
            if (cam.enabled)
            {
                currentCamera = cam;
                framingTransposer = currentCamera.GetComponent<CinemachinePositionComposer>();
                cameraOffset = currentCamera.GetComponent<CinemachineCameraOffset>();
                break;
            }
        }


        if (framingTransposer == null)
        {
            Debug.LogWarning("No CinemachineFramingTransposer found on any active virtual camera.");
        }
    }

    #region Pan Camera

    public void PanCameraOnContact(float panDistance, float panTime, PanDirection panDirection, bool panToStartingPos)
    {
        panCameraCoroutine = StartCoroutine(PanCamera(panDistance, panTime, panDirection, panToStartingPos));
    }

    private IEnumerator PanCamera(float panDistance, float panTime, PanDirection panDirection, bool panToStartingPos)
    {
        Vector2 endPos = Vector2.zero;
        Vector2 startingPos = Vector2.zero;

        if (!panToStartingPos)
        {
            switch (panDirection)
            {
                case PanDirection.Up:
                    endPos = Vector2.up;
                    break;
                case PanDirection.Down:
                    endPos = Vector2.down;
                    break;
                case PanDirection.Left:
                    endPos = Vector2.left;
                    break;
                case PanDirection.Right:
                    endPos = Vector2.right;
                    break;
            }

            endPos *= panDistance;

            startingPos = startingTrackedObjectOffset;

            endPos += startingPos;
        }
        else
        {
            startingPos = cameraOffset.Offset;
            endPos = startingTrackedObjectOffset;
        }

        float elapsedTime = 0f;
        while (elapsedTime < panTime)
        {
            elapsedTime += Time.deltaTime;

            Vector3 panLerp = Vector3.Lerp(startingPos, endPos, (elapsedTime / panTime));
            cameraOffset.Offset = panLerp;

            yield return null;
        }
    }

    #endregion Pan Camera

    #region Swap Cameras

    public void SwapCamera(CinemachineCamera cameraFromLeft, CinemachineCamera cameraFromRight, Vector2 triggerExitDirection, bool horizontal)
    {
        if (horizontal)
        {
            if (currentCamera == cameraFromLeft && triggerExitDirection.x > 0f)
            {
                cameraFromRight.enabled = true;
                cameraFromLeft.enabled = false;

                currentCamera = cameraFromRight;
                framingTransposer = currentCamera.GetComponent<CinemachinePositionComposer>();
                cameraOffset = currentCamera.GetComponent<CinemachineCameraOffset>();
            }
            else if (currentCamera == cameraFromRight && triggerExitDirection.x < 0f)
            {
                cameraFromRight.enabled = false;
                cameraFromLeft.enabled = true;

                currentCamera = cameraFromLeft;
                framingTransposer = currentCamera.GetComponent<CinemachinePositionComposer>();
                cameraOffset = currentCamera.GetComponent<CinemachineCameraOffset>();

            }
        }
        else
        {
            if (currentCamera == cameraFromLeft && triggerExitDirection.y > 0f)
            {
                Debug.Log("camera right enabled");
                cameraFromRight.enabled = true;
                cameraFromLeft.enabled = false;

                currentCamera = cameraFromRight;
                framingTransposer = currentCamera.GetComponent<CinemachinePositionComposer>();
                cameraOffset = currentCamera.GetComponent<CinemachineCameraOffset>();
            }
            else if (currentCamera == cameraFromRight && triggerExitDirection.y < 0f)
            {
                Debug.Log("camera left enabled");
                cameraFromRight.enabled = false;
                cameraFromLeft.enabled = true;

                currentCamera = cameraFromLeft;
                framingTransposer = currentCamera.GetComponent<CinemachinePositionComposer>();
                cameraOffset = currentCamera.GetComponent<CinemachineCameraOffset>();
            }
        }
    }

    #endregion

    #region Zoom Camera
    public void ZoomCamera(float zoomAmount, float zoomTime, bool shouldZoomBack)
    {
        if (framingTransposer == null) return;

        if (panCameraCoroutine != null)
        {
            StopCoroutine(panCameraCoroutine);
        }

        panCameraCoroutine = StartCoroutine(ZoomCameraCoroutine(zoomAmount, zoomTime, shouldZoomBack));
    }

    private IEnumerator ZoomCameraCoroutine(float zoomAmount, float zoomTime, bool shouldZoomBack)
    {
        float initialZoom = framingTransposer.CameraDistance; // Initial camera distance (for framing transposer)
        float targetZoom = initialZoom + zoomAmount; // Calculate target zoom

        float elapsedTime = 0f;

        // Smoothly zoom towards target zoom
        while (elapsedTime < zoomTime)
        {
            framingTransposer.CameraDistance = Mathf.Lerp(initialZoom, targetZoom, elapsedTime / zoomTime);
            elapsedTime += Time.deltaTime;
            yield return null; // Wait for the next frame
        }

        // Set final zoom value
        framingTransposer.CameraDistance = targetZoom;

        // If we need to zoom back, do so after zoomTime
        if (shouldZoomBack)
        {
            float zoomBackTime = zoomTime; // Zoom back time can be the same as zoom-in time or adjusted if you prefer a different time
            elapsedTime = 0f;

            // Smoothly zoom back to original position
            while (elapsedTime < zoomBackTime)
            {
                framingTransposer.CameraDistance = Mathf.Lerp(targetZoom, initialZoom, elapsedTime / zoomBackTime);
                elapsedTime += Time.deltaTime;
                yield return null; // Wait for the next frame
            }

            // Set the camera back to the initial zoom position
            framingTransposer.CameraDistance = initialZoom;
        }
    }
}

#endregion

