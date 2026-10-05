using UnityEngine;

public class PawnManager : MonoBehaviour
{
    public static PawnManager instance;

    public int pawnCount = 0;
    public int totalPawns = 8;

    public bool IsUnlocked => pawnCount >= totalPawns;

    public DetectRotate detectRotate;

    [Header("Camera Shake at 8 Pawns")]
    [SerializeField] private Camera shakeCamera;
    [SerializeField, Min(0f), Tooltip("How long the camera shakes, in seconds.")]
    private float shakeDuration = 0.6f;
    [SerializeField, Min(0f), Tooltip("Maximum camera offset in local units. Set to 0 to disable the shake.")]
    private float shakeAmplitude = 0.15f;

    private bool hasTriggeredCameraShake;
    private Transform shakingCamera;
    private Vector3 appliedShakeOffset;
    private float shakeTimeRemaining;
    private float activeShakeDuration;
    private float activeShakeAmplitude;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ResetCameraShakes()
    {
        // This project enters Play without reloading the scene or script domain.
        foreach (PawnManager manager in FindObjectsByType<PawnManager>(FindObjectsInactive.Include))
        {
            manager.StopCameraShake();
            manager.hasTriggeredCameraShake = false;
        }
    }

    private void Awake()
    {
        instance = this;
    }

    public void PawnFinishedMoving()
    {
        pawnCount++;

        Debug.Log("Pawn moved: " + pawnCount + " / " + totalPawns);

        if (IsUnlocked && detectRotate != null)
        {
            detectRotate.enabled = true;
        }

        TryTriggerCameraShake();
    }

    private void LateUpdate()
    {
        // Also support changing the public count directly in the Inspector.
        TryTriggerCameraShake();
        UpdateCameraShake(Time.deltaTime);
    }

    private void TryTriggerCameraShake()
    {
        if (hasTriggeredCameraShake || pawnCount != 8 || !isActiveAndEnabled)
        {
            return;
        }

        hasTriggeredCameraShake = true;
        Camera targetCamera = shakeCamera != null ? shakeCamera : Camera.main;
        if (targetCamera == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                targetCamera = player.GetComponentInChildren<Camera>();
            }
        }

        if (targetCamera == null || shakeDuration <= 0f || shakeAmplitude <= 0f)
        {
            return;
        }

        shakingCamera = targetCamera.transform;
        activeShakeDuration = shakeDuration;
        activeShakeAmplitude = shakeAmplitude;
        shakeTimeRemaining = activeShakeDuration;
    }

    private void UpdateCameraShake(float deltaTime)
    {
        // Remove only our previous offset, preserving movement from other scripts.
        RemoveShakeOffset();
        if (shakingCamera == null)
        {
            shakeTimeRemaining = 0f;
            return;
        }

        shakeTimeRemaining = Mathf.Max(0f, shakeTimeRemaining - deltaTime);
        if (shakeTimeRemaining <= 0f)
        {
            shakingCamera = null;
            return;
        }

        float noiseTime = (activeShakeDuration - shakeTimeRemaining) * 30f;
        Vector2 noise = new Vector2(
            Mathf.PerlinNoise(noiseTime, 17.13f) * 2f - 1f,
            Mathf.PerlinNoise(41.73f, noiseTime) * 2f - 1f);
        noise = Vector2.ClampMagnitude(noise, 1f);
        float strength = activeShakeAmplitude * (shakeTimeRemaining / activeShakeDuration);
        appliedShakeOffset = new Vector3(noise.x, noise.y, 0f) * strength;
        shakingCamera.localPosition += appliedShakeOffset;
    }

    private void RemoveShakeOffset()
    {
        if (shakingCamera != null)
        {
            shakingCamera.localPosition -= appliedShakeOffset;
        }

        appliedShakeOffset = Vector3.zero;
    }

    private void OnDisable()
    {
        StopCameraShake();
    }

    private void StopCameraShake()
    {
        RemoveShakeOffset();
        shakingCamera = null;
        shakeTimeRemaining = 0f;
    }
}
