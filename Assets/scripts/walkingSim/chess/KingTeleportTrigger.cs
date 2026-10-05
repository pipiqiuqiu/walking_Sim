using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class KingTeleportTrigger : MonoBehaviour
{
    [SerializeField] private KingTeleportTrigger pairedKing;
    [SerializeField] private Collider targetSurface;
    [Tooltip("Face normal recorded during setup. Kept for existing scene data.")]
    [SerializeField] private Vector3 localSurfaceNormal = Vector3.up;
    [Tooltip("Arrival point recorded during setup. The live king position is used at runtime.")]
    [SerializeField] private Vector3 localArrivalPoint;
    [Tooltip("Minimum time between transfers. The arriving king stays locked until the player leaves its trigger.")]
    [SerializeField, Min(0f)] private float reentryCooldown = 0.5f;
    [Tooltip("Search nearby positions on this same face if the exact arrival point is occupied. In world units.")]
    [SerializeField, Min(0f)] private float arrivalSearchRadius = 2f;

    private BoxCollider triggerCollider;
    private readonly List<PlayerMovement> releasedPlayers = new List<PlayerMovement>();
    private static readonly Vector3[] surfaceDirections =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

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
    public Collider TargetSurface => SolidTargetSurface;
    public Vector3 LocalSurfaceNormal => localSurfaceNormal;
    public Vector3 LocalArrivalPoint => localArrivalPoint;
    public Vector3 WorldNormal => TryGetCurrentArrival(out _, out Vector3 normal)
        ? normal : transform.up;
    public Vector3 WorldArrivalPoint => TryGetCurrentArrival(out Vector3 point, out _)
        ? point : transform.position;

    private Collider SolidTargetSurface
    {
        get
        {
            if (targetSurface == null || !targetSurface.isTrigger) return targetSurface;
            // The glass room's kings reference its interaction trigger. The
            // solid mesh on that same block is the actual walkable surface.
            MeshCollider mesh = targetSurface.GetComponent<MeshCollider>();
            return mesh != null && !mesh.isTrigger ? mesh : null;
        }
    }

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
            || TargetSurface == null || pairedKing.TargetSurface != TargetSurface)
            return false;
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null || !player.isActiveAndEnabled || !HasPlayerTag(other.transform))
            return false;
        if (arrivalLocks.TryGetValue(player, out ArrivalLock previous)
            && (Time.unscaledTime < previous.notBefore
                || (!previous.hasLeft && previous.destination == this)))
            return false;

        if (!pairedKing.TryGetCurrentArrival(out Vector3 footPoint, out Vector3 normal))
            return false;
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
        if (pairedKing.TryPlacePlayer(player, footPoint, normal, forward))
            return true;
        if (previous == null) arrivalLocks.Remove(player);
        else arrivalLocks[player] = previous;
        return false;
    }

    private bool TryGetCurrentArrival(out Vector3 footPoint, out Vector3 normal)
    {
        footPoint = default;
        normal = default;
        Collider surface = SolidTargetSurface;
        if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy)
            return false;

        if (triggerCollider == null) triggerCollider = GetComponent<BoxCollider>();
        if (triggerCollider == null || !triggerCollider.enabled)
            return false;

        Physics.SyncTransforms();
        Vector3 marker = triggerCollider.transform.TransformPoint(triggerCollider.center);
        float reach = Mathf.Max(1f, triggerCollider.bounds.extents.magnitude + 0.25f);
        float bestDistance = float.PositiveInfinity;
        Vector3 bestPoint = default;
        Vector3 bestNormal = default;
        TryFace(transform.up);
        Transform surfaceTransform = surface.transform;
        foreach (Vector3 direction in surfaceDirections)
            TryFace(surfaceTransform.TransformDirection(direction));
        footPoint = bestPoint;
        normal = bestNormal;
        return bestDistance < float.PositiveInfinity;

        void TryFace(Vector3 direction)
        {
            if (!surface.Raycast(new Ray(marker + direction * reach, -direction),
                    out RaycastHit hit, reach * 2f)
                || Vector3.Dot(hit.normal, direction) < 0.99f)
                return;

            float distance = (hit.point - marker).sqrMagnitude;
            if (distance >= bestDistance || distance > reach * reach) return;
            bestDistance = distance;
            bestPoint = hit.point;
            bestNormal = hit.normal;
        }
    }

    private bool TryPlacePlayer(PlayerMovement player, Vector3 footPoint, Vector3 normal, Vector3 forward)
    {
        Collider surface = SolidTargetSurface;
        if (surface == null) return false;
        if (player.TeleportToSurface(surface, footPoint, normal, forward)) return true;
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
                Vector3 candidate = footPoint + offset;
                if (surface.Raycast(new Ray(candidate + normal * 0.05f, -normal),
                        out RaycastHit hit, 0.1f)
                    && Vector3.Dot(hit.normal, normal) >= 0.99f
                    && player.TeleportToSurface(surface, hit.point, normal, forward)) return true;
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
