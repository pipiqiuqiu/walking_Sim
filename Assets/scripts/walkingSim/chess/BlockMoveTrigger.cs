using System.Collections.Generic;
using UnityEngine;

public class BlockMoveTrigger : MonoBehaviour
{
    public Transform targetObject;
    public PawnManager pawnManager;
    public enum MoveAxis
    {
        X,
        Y,
        Z
    }

    [Tooltip("Movement follows this axis of the target itself, including layer rotations.")]
    public MoveAxis moveAxis = MoveAxis.Y;

    [Tooltip("Signed travel distance in the target's initial parent units. Temporary reparenting does not change the travel distance.")]
    public float moveDistance = 2f;
    [Tooltip("Speed in the target's initial parent units per second while triggered.")]
    public float moveSpeed = 1f;

    private Transform boundTarget;
    private Vector3 movementUnitScale = Vector3.one;
    private MovementState movement;
    private bool isTriggered = false;
    private PlayerCameraControl playerCameraControl;
    private bool isCameraShaking;

    private sealed class MovementState
    {
        internal MoveAxis axis;
        internal float distance;
        internal float speed;
        internal float unitScale;
        internal float timer;
        internal float previousAmount;
        internal int lastFrame = int.MinValue;
    }

    private static readonly Dictionary<Transform, List<MovementState>> movements
        = new Dictionary<Transform, List<MovementState>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMovements()
    {
        movements.Clear();
    }

    void Start()
    {
        BindTarget();
    }

    void Update()
    {
        bool shouldMove = pawnManager != null && pawnManager.IsUnlocked
            && isTriggered && BindTarget() && Mathf.Abs(moveDistance) >= 0.000001f
            && Mathf.Abs(moveSpeed) >= 0.000001f;
        if (shouldMove)
            MoveObject(Time.deltaTime, Time.frameCount);

        SetCameraShake(shouldMove);
    }

    private void SetCameraShake(bool active)
    {
        if (!active && !isCameraShaking)
            return;

        isCameraShaking = active;
        if (playerCameraControl != null)
            playerCameraControl.SetBlockMovementShake(this, active);
    }

    bool BindTarget()
    {
        if (targetObject == null)
            return false;
        if (boundTarget != targetObject)
        {
            boundTarget = targetObject;
            movement = null;
            Transform initialParent = targetObject.parent;
            movementUnitScale = initialParent == null ? Vector3.one : new Vector3(
                initialParent.TransformVector(Vector3.right).magnitude,
                initialParent.TransformVector(Vector3.up).magnitude,
                initialParent.TransformVector(Vector3.forward).magnitude);
        }
        if (!movements.TryGetValue(targetObject, out List<MovementState> states))
        {
            states = new List<MovementState>();
            movements.Add(targetObject, states);
        }
        if (movement != null && states.Contains(movement)
            && movement.axis == moveAxis && movement.distance == moveDistance && movement.speed == moveSpeed)
            return true;
        movement = states.Find(state => state.axis == moveAxis
            && state.distance == moveDistance && state.speed == moveSpeed);
        if (movement == null)
        {
            movement = new MovementState
            {
                axis = moveAxis,
                distance = moveDistance,
                speed = moveSpeed,
                unitScale = moveAxis == MoveAxis.X ? movementUnitScale.x
                    : moveAxis == MoveAxis.Z ? movementUnitScale.z : movementUnitScale.y
            };
            states.Add(movement);
        }
        return true;
    }

    void MoveObject(float deltaTime, int frameIndex)
    {
        if (!BindTarget() || Mathf.Abs(moveDistance) < 0.000001f || movement.lastFrame == frameIndex)
            return;
        // Matching buttons control one trip, even when both are occupied.
        movement.lastFrame = frameIndex;
        movement.timer += Mathf.Max(0f, deltaTime) * moveSpeed;
        float moveAmount = Mathf.PingPong(movement.timer, Mathf.Abs(moveDistance))
            * Mathf.Sign(moveDistance);
        float delta = moveAmount - movement.previousAmount;
        movement.previousAmount = moveAmount;

        Vector3 direction;
        switch (moveAxis)
        {
            case MoveAxis.X:
                direction = targetObject.right;
                break;
            case MoveAxis.Z:
                direction = targetObject.forward;
                break;
            default:
                direction = targetObject.up;
                break;
        }
        // Layer rotation changes the parent and position. Add only this frame's
        // travel in the target's current orientation, preserving those changes.
        targetObject.position += direction * (delta * movement.unitScale);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isTriggered = true;
            playerCameraControl = other.GetComponentInParent<PlayerCameraControl>();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isTriggered = false;
            SetCameraShake(false);
            playerCameraControl = null;
        }
    }

    private void OnDisable()
    {
        SetCameraShake(false);
    }
}
