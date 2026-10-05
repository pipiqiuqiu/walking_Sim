using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class KingTeleportTrigger : MonoBehaviour
{
    [SerializeField] private KingTeleportTrigger pairedKing;
    [SerializeField] private Collider targetSurface;
    [Tooltip("Outward face normal in the owning surface's local space.")]
    [SerializeField] private Vector3 localSurfaceNormal = Vector3.up;
    [Tooltip("The player's foot point on this king's face, in surface-local space.")]
    [SerializeField] private Vector3 localArrivalPoint;
    [Tooltip("Minimum time between transfers. The arriving king stays locked until the player leaves its trigger.")]
    [SerializeField, Min(0f)] private float reentryCooldown = 0.5f;
    [Tooltip("Search nearby positions on this same face if the exact arrival point is occupied. In world units.")]
    [SerializeField, Min(0f)] private float arrivalSearchRadius = 2f;

    private BoxCollider triggerCollider;
    private readonly List<PlayerMovement> releasedPlayers = new List<PlayerMovement>();

    private sealed class ArrivalLock
    {
        internal KingTeleportTrigger destination;
        internal float notBefore;
        internal bool hasLeft;
        internal Collider[] playerColliders;
    }
    private static readonly Dictionary<PlayerMovement, ArrivalLock> arrivalLocks
        = new Dictionary<PlayerMovement, ArrivalLock>();

    public KingTeleportTrigger PairedKing => pairedKing;
    public Collider TargetSurface => targetSurface;
    public Vector3 LocalSurfaceNormal => localSurfaceNormal;
    public Vector3 LocalArrivalPoint => localArrivalPoint;
    public Vector3 WorldNormal => targetSurface == null ? transform.up
        : targetSurface.transform.worldToLocalMatrix.transpose.MultiplyVector(localSurfaceNormal).normalized;
    public Vector3 WorldArrivalPoint => targetSurface == null ? transform.position
        : targetSurface.transform.TransformPoint(localArrivalPoint);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTransfers()
    {
        arrivalLocks.Clear();
    }

    public void Configure(KingTeleportTrigger partner, Collider surface, Vector3 normal, Vector3 footPoint)
    {
        pairedKing = partner;
        targetSurface = surface;
        localSurfaceNormal = normal.normalized;
        localArrivalPoint = footPoint;
    }

    private void Awake()
    {
        triggerCollider = GetComponent<BoxCollider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryTeleport(other);
    }

    public bool TryTeleport(Collider other)
    {
        if (!isActiveAndEnabled || other == null || pairedKing == null || pairedKing == this
            || !pairedKing.isActiveAndEnabled || pairedKing.pairedKing != this
            || targetSurface == null || pairedKing.targetSurface != targetSurface)
            return false;
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null || !player.isActiveAndEnabled || !HasPlayerTag(other.transform))
            return false;
        if (arrivalLocks.TryGetValue(player, out ArrivalLock previous)
            && (Time.unscaledTime < previous.notBefore
                || (!previous.hasLeft && previous.destination == this)))
            return false;

        Vector3 normal = pairedKing.WorldNormal;
        Vector3 forward = Quaternion.FromToRotation(player.transform.up, normal) * player.transform.forward;
        ArrivalLock transfer = new ArrivalLock
        {
            destination = pairedKing,
            notBefore = Time.unscaledTime + Mathf.Max(reentryCooldown, pairedKing.reentryCooldown),
            playerColliders = player.GetComponentsInChildren<Collider>(true)
        };
        // Reserve before changing the pose so multiple colliders cannot send
        // the same player back during the same physics step.
        arrivalLocks[player] = transfer;
        if (pairedKing.TryPlacePlayer(player, normal, forward))
            return true;
        if (previous == null) arrivalLocks.Remove(player);
        else arrivalLocks[player] = previous;
        return false;
    }

    private bool TryPlacePlayer(PlayerMovement player, Vector3 normal, Vector3 forward)
    {
        Vector3 footPoint = WorldArrivalPoint;
        if (player.TeleportToSurface(targetSurface, footPoint, normal, forward)) return true;
        if (arrivalSearchRadius <= 0f || normal.sqrMagnitude < 0.0001f) return false;
        Vector3 tangent = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 across = Vector3.Cross(normal, tangent).normalized;
        for (int ring = 1; ring <= 2; ring++)
        {
            float radius = arrivalSearchRadius * ring * 0.5f;
            for (int sample = 0; sample < 8; sample++)
            {
                float angle = sample * Mathf.PI * 0.25f;
                Vector3 offset = (tangent * Mathf.Cos(angle) + across * Mathf.Sin(angle)) * radius;
                if (player.TeleportToSurface(targetSurface, footPoint + offset, normal, forward)) return true;
            }
        }
        return false;
    }

    private void LateUpdate()
    {
        releasedPlayers.Clear();
        foreach (KeyValuePair<PlayerMovement, ArrivalLock> entry in arrivalLocks)
        {
            ArrivalLock transfer = entry.Value;
            if (transfer.destination != this) continue;
            if (entry.Key == null) { releasedPlayers.Add(entry.Key); continue; }
            if (!transfer.hasLeft && !OverlapsPlayer(transfer.playerColliders)) transfer.hasLeft = true;
            if (transfer.hasLeft && Time.unscaledTime >= transfer.notBefore) releasedPlayers.Add(entry.Key);
        }
        foreach (PlayerMovement player in releasedPlayers) arrivalLocks.Remove(player);
    }

    private bool OverlapsPlayer(Collider[] colliders)
    {
        if (triggerCollider == null) triggerCollider = GetComponent<BoxCollider>();
        if (triggerCollider == null || !triggerCollider.enabled || colliders == null) return false;
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (Physics.ComputePenetration(triggerCollider, triggerCollider.transform.position, triggerCollider.transform.rotation,
                collider, collider.transform.position, collider.transform.rotation, out _, out float distance)
                && distance > 0f) return true;
        }
        return false;
    }

    private void OnDisable()
    {
        releasedPlayers.Clear();
        foreach (KeyValuePair<PlayerMovement, ArrivalLock> entry in arrivalLocks)
            if (entry.Value.destination == this) releasedPlayers.Add(entry.Key);
        foreach (PlayerMovement player in releasedPlayers) arrivalLocks.Remove(player);
    }

    private static bool HasPlayerTag(Transform collider)
    {
        for (Transform current = collider; current != null; current = current.parent)
            if (current.CompareTag("Player")) return true;
        return false;
    }
}
