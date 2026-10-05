using UnityEngine;

[DefaultExecutionOrder(100)]
public class ChessHorseEscape : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform board;
    [SerializeField] private PawnManager pawnManager;
    [Tooltip("The allowed landing points. If empty, uses the board's direct children.")]
    [SerializeField] private Transform[] jumpPoints = new Transform[0];

    [Header("Escape")]
    [Tooltip("Start escaping when the player is closer than this many world units.")]
    [SerializeField, Min(0f)] private float detectionDistance = 25f;

    [Header("Facing and destination")]
    [Tooltip("The model's visual forward direction in the horse's local space.")]
    [SerializeField] private Vector3 modelLocalForward = Vector3.forward;
    [Tooltip("0 prefers short jumps; 1 prefers points directly ahead. Destinations must still be farther from the player.")]
    [SerializeField, Range(0f, 1f)] private float facingPreference = 0.8f;

    [Header("Jump")]
    [SerializeField, Min(0.01f)] private float jumpDuration = 2f;
    [Tooltip("Maximum distance above the board surface, in world units.")]
    [SerializeField, Min(0f)] private float jumpHeight = 8f;
    [Tooltip("Surface normal in the board's local space. This wall board faces local -Z.")]
    [SerializeField] private Vector3 boardLocalJumpDirection = Vector3.back;

    private Transform currentPoint;
    private Transform targetPoint;
    private Vector3 landingOffset;
    private Quaternion boardRelativeRotation;
    private Vector3 jumpStartLocal;
    private float jumpElapsed;
    private bool isJumping;
    private bool initialized;

    private void Start()
    {
        if (board == null)
        {
            Debug.LogWarning("ChessHorseEscape needs a board reference.", this);
            enabled = false;
            return;
        }

        if (jumpPoints == null || jumpPoints.Length == 0)
        {
            jumpPoints = new Transform[board.childCount];
            for (int i = 0; i < jumpPoints.Length; i++)
                jumpPoints[i] = board.GetChild(i);
        }

        currentPoint = FindClosestPoint();
        if (currentPoint == null)
        {
            Debug.LogWarning("ChessHorseEscape needs at least one landing point.", this);
            enabled = false;
            return;
        }

        // Keep the model's original pivot/base offset when landing on each marker.
        landingOffset = board.InverseTransformPoint(transform.position)
            - board.InverseTransformPoint(currentPoint.position);
        boardRelativeRotation = Quaternion.Inverse(board.rotation) * transform.rotation;
        initialized = true;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }
    }

    private void LateUpdate()
    {
        if (pawnManager == null)
            return;

        if (!pawnManager.IsUnlocked)
            return;

        Tick(Time.deltaTime);
    }

    private void Tick(float deltaTime)
    {
        if (!initialized || board == null)
            return;

        transform.rotation = board.rotation * boardRelativeRotation;

        if (isJumping)
        {
            if (!IsUsablePoint(targetPoint))
            {
                CancelJump();
                FaceAwayFromPlayer();
                return;
            }

            jumpElapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(jumpElapsed / Mathf.Max(0.01f, jumpDuration));
            Vector3 targetLocal = GetLandingLocal(targetPoint);
            Vector3 normal = GetBoardNormal();
            float arcHeight = 4f * Mathf.Max(0f, jumpHeight) * progress * (1f - progress);
            transform.position = board.TransformPoint(Vector3.Lerp(jumpStartLocal, targetLocal, progress))
                + normal * arcHeight;
            FaceAwayFromPlayer();

            if (progress >= 1f)
            {
                currentPoint = targetPoint;
                targetPoint = null;
                isJumping = false;
            }
            return;
        }

        if (IsUsablePoint(currentPoint))
            transform.position = board.TransformPoint(GetLandingLocal(currentPoint));

        if (player == null || !player.gameObject.activeInHierarchy
            || (player.position - transform.position).sqrMagnitude >= detectionDistance * detectionDistance)
            return;

        FaceAwayFromPlayer();
        Transform nextPoint = FindEscapePoint();
        if (nextPoint == null)
            return;

        // Reconsider the player's current position only after finishing this jump.
        targetPoint = nextPoint;
        jumpStartLocal = board.InverseTransformPoint(transform.position);
        jumpElapsed = 0f;
        isJumping = true;
    }

    private Transform FindClosestPoint()
    {
        Transform closest = null;
        float closestDistance = float.PositiveInfinity;
        foreach (Transform point in jumpPoints)
        {
            if (!IsUsablePoint(point))
                continue;

            float distance = (point.position - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = point;
            }
        }
        return closest;
    }

    private Transform FindEscapePoint()
    {
        Transform nextPoint = null;
        float bestScore = float.NegativeInfinity;
        float shortestJump = float.PositiveInfinity;
        Vector3 normal = GetBoardNormal();
        Vector3 facing = Vector3.ProjectOnPlane(transform.TransformDirection(modelLocalForward), normal).normalized;
        float preference = Mathf.Clamp01(facingPreference);
        float distanceScale = Mathf.Max(0.01f, detectionDistance);
        // A small world-space margin avoids bouncing between nearly equal distances.
        float minimumDistance = Vector3.Distance(transform.position, player.position) + 0.01f;
        float minimumDistanceSquared = minimumDistance * minimumDistance;

        foreach (Transform point in jumpPoints)
        {
            if (!IsUsablePoint(point) || point == currentPoint)
                continue;

            Vector3 landingPosition = board.TransformPoint(GetLandingLocal(point));
            if ((landingPosition - player.position).sqrMagnitude <= minimumDistanceSquared)
                continue;

            float jumpDistance = (landingPosition - transform.position).sqrMagnitude;
            if (jumpDistance <= 0.0001f)
                continue;

            Vector3 direction = Vector3.ProjectOnPlane(landingPosition - transform.position, normal).normalized;
            float alignment = (Vector3.Dot(facing, direction) + 1f) * 0.5f;
            float proximity = distanceScale / (distanceScale + Mathf.Sqrt(jumpDistance));
            float score = preference * alignment + (1f - preference) * proximity;
            if (score > bestScore + 0.0001f
                || (Mathf.Abs(score - bestScore) <= 0.0001f && jumpDistance < shortestJump))
            {
                bestScore = score;
                shortestJump = jumpDistance;
                nextPoint = point;
            }
        }
        return nextPoint;
    }

    private Vector3 GetBoardNormal()
    {
        return board.TransformDirection(boardLocalJumpDirection.normalized);
    }

    private void FaceAwayFromPlayer()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
            return;

        Vector3 normal = GetBoardNormal();
        Vector3 away = Vector3.ProjectOnPlane(transform.position - player.position, normal);
        Vector3 forward = Vector3.ProjectOnPlane(transform.TransformDirection(modelLocalForward), normal);
        // When the player is directly above/below the pivot, keep the last valid heading.
        if (away.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
            return;

        float angle = Vector3.SignedAngle(forward, away, normal);
        transform.rotation = Quaternion.AngleAxis(angle, normal) * transform.rotation;
        boardRelativeRotation = Quaternion.Inverse(board.rotation) * transform.rotation;
    }

    private Vector3 GetLandingLocal(Transform point)
    {
        return board.InverseTransformPoint(point.position) + landingOffset;
    }

    private static bool IsUsablePoint(Transform point)
    {
        return point != null && point.gameObject.activeInHierarchy;
    }

    private void CancelJump()
    {
        if (board != null && IsUsablePoint(currentPoint))
            transform.position = board.TransformPoint(GetLandingLocal(currentPoint));
        targetPoint = null;
        isJumping = false;
    }

    private void OnDisable()
    {
        if (isJumping)
            CancelJump();
    }

    private void OnValidate()
    {
        detectionDistance = Mathf.Max(0f, detectionDistance);
        jumpDuration = Mathf.Max(0.01f, jumpDuration);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        facingPreference = Mathf.Clamp01(facingPreference);
        if (boardLocalJumpDirection.sqrMagnitude < 0.0001f)
            boardLocalJumpDirection = Vector3.back;
        if (modelLocalForward.sqrMagnitude < 0.0001f)
            modelLocalForward = Vector3.forward;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionDistance);
    }
}
