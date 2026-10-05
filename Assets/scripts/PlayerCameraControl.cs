using System.Collections.Generic;
using UnityEngine;

public class PlayerCameraControl : MonoBehaviour
{

    [SerializeField] private Transform cameraHolder;
    [SerializeField] private float mouseSensitivity;
    [SerializeField] private float stickSensitivity;

    [Header("Block Movement Camera Shake")]
    [SerializeField, Min(0f)] private float blockShakeAmplitude = 0.05f;
    [SerializeField, Min(0f)] private float blockShakeFrequency = 30f;

    private float rotationX = 0f;
    private float inputX;
    private float inputY;
    private Controls controls;
    private readonly HashSet<BlockMoveTrigger> blockShakeSources = new HashSet<BlockMoveTrigger>();
    private Transform shakingCamera;
    private Vector3 appliedShakeOffset;
    private float shakeTime;

    public void SetBlockMovementShake(BlockMoveTrigger source, bool active)
    {
        if (source == null)
            return;

        if (active)
            blockShakeSources.Add(source);
        else
            blockShakeSources.Remove(source);

        if (blockShakeSources.Count == 0)
            RemoveShakeOffset();
    }

    void Start()
    {
        //lock mouse and disable cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        //get the controls component
        controls = GetComponent<Controls>();
    }

    void Update()
    {
        //get inputX based on mouse or right stick movement
        inputX = controls.LookMouseInput().x * mouseSensitivity;
        inputX += controls.LookStickInput().x * stickSensitivity * Time.deltaTime;

        //get inputY based on mouse or right stick movement
        inputY = controls.LookMouseInput().y * mouseSensitivity;
        inputY += controls.LookStickInput().y * stickSensitivity * Time.deltaTime;
        
        //rotate the entire player left and right based on inputX
        transform.Rotate(Vector3.up * inputX, Space.Self);
        
        //rotate the camera up and down based on inputY, clamped to 90 degrees up and down
        rotationX -= inputY; //invert the inputY so that moving the mouse up looks up and moving the mouse down looks down
        rotationX = Mathf.Clamp(rotationX, -90f, 90f); //clamp the rotationX to 90 degrees up and down
        cameraHolder.localRotation = Quaternion.Euler(rotationX, 0f, 0f); //rotate the camera up and down based on rotationX  
    }

    void LateUpdate()
    {
        // Keep the offset separate from normal camera movement and other shake effects.
        RemoveShakeOffset();
        if (blockShakeSources.Count == 0 || blockShakeAmplitude <= 0f || cameraHolder == null)
            return;

        if (shakingCamera == null)
        {
            Camera playerCamera = cameraHolder.GetComponentInChildren<Camera>(true);
            if (playerCamera == null)
                return;
            shakingCamera = playerCamera.transform;
        }

        shakeTime += Time.deltaTime * blockShakeFrequency;
        Vector2 noise = new Vector2(
            Mathf.PerlinNoise(shakeTime, 17.13f) * 2f - 1f,
            Mathf.PerlinNoise(41.73f, shakeTime) * 2f - 1f);
        appliedShakeOffset = new Vector3(noise.x, noise.y, 0f)
            * blockShakeAmplitude;
        shakingCamera.localPosition += appliedShakeOffset;
    }

    private void RemoveShakeOffset()
    {
        if (shakingCamera != null)
            shakingCamera.localPosition -= appliedShakeOffset;
        appliedShakeOffset = Vector3.zero;
    }

    private void OnDisable()
    {
        RemoveShakeOffset();
        blockShakeSources.Clear();
        shakingCamera = null;
        shakeTime = 0f;
    }
}

