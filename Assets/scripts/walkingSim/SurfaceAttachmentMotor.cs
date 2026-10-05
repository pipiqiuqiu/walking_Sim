using UnityEngine;

// Moves a full capsule in the surface's frame, with gravity along local down.
// The regular CharacterController stays disabled while this motor is in use.
internal sealed class SurfaceAttachmentMotor
{
    private const float MinimumFloorAlignment = 0.98f;
    private const float CastClearance = 0.002f;
    private const int SlideIterations = 3;
    private const float BlockedFallRecoveryDelay = 1f;
    private const float MaximumWalkingStepDistance = 0.025f;

    private readonly CharacterController controller;
    private readonly Transform player;
    private readonly LayerMask groundMask;
    private readonly float initialSurfaceSearchDistance;
    private readonly float groundProbeDistance;
    private readonly bool allowSurfaceTransitions;
    private readonly float surfaceTransitionDuration;
    private readonly float surfaceTransitionReach;
    private readonly float jumpStartingVelocity;
    private readonly float jumpGravity;
    private readonly float jumpHeldTimerMax;
    private readonly float fallSpeedMax;
    private float maximumStepHeight;
    private float maximumJumpSnapDistance;
    private float maximumJumpWallAttachDistance;
    private readonly RaycastHit[] castHits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[32];

    private Collider surface;
    private Vector3 localPlayerPosition;
    private Quaternion previousSurfaceRotation;
    private bool transitioning;
    private bool airborneWallTransition;
    private float transitionElapsed;
    private Vector3 transitionPivotLocal;
    private Vector3 transitionOffsetLocal;
    private Vector3 transitionStartUpLocal;
    private Vector3 transitionEndUpLocal;
    private float transitionHalfSegment;
    private Collider transitionSource;
    private Vector3 transitionSourceCenterLocal;
    private Vector3 transitionSourceUpLocal;
    private bool recoveringFromTransition;
    private bool jumping;
    private bool falling;
    private bool fallFromJump;
    private float fallVelocity;
    private float blockedFallTime;
    private bool jumpReleased;
    private float jumpVelocity;
    private float jumpHeldTimer;
    private float jumpElapsed;
    private bool steppingOverLedge;
    private Collider stepSource;
    private Vector3 stepSourcePointLocal;
    private float stepSettleVelocity;
    private Collider lastSupportedSurface;
    private Vector3 lastSupportedCenterLocal;
    private Vector3 lastSupportedPointLocal;
    private Vector3 lastSupportedUpLocal;

    internal SurfaceAttachmentMotor(CharacterController controller, LayerMask groundMask,
        float initialSurfaceSearchDistance, float groundProbeDistance,
        bool allowSurfaceTransitions = false, float surfaceTransitionDuration = 0.4f,
        float surfaceTransitionReach = 0.3f, float jumpStartingVelocity = 6f,
        float jumpGravity = 25f, float jumpHeldTimerMax = 0.25f, float fallSpeedMax = 40f,
        float maximumStepHeight = 0.4f, float maximumJumpSnapDistance = 2f,
        float maximumJumpWallAttachDistance = 2f)
    {
        this.controller = controller;
        player = controller.transform;
        this.groundMask = groundMask;
        this.initialSurfaceSearchDistance = Mathf.Max(0f, initialSurfaceSearchDistance);
        this.groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
        this.allowSurfaceTransitions = allowSurfaceTransitions;
        this.surfaceTransitionDuration = Mathf.Max(0.05f, surfaceTransitionDuration);
        this.surfaceTransitionReach = Mathf.Max(0f, surfaceTransitionReach);
        this.jumpStartingVelocity = Mathf.Max(0f, jumpStartingVelocity);
        this.jumpGravity = Mathf.Max(0.01f, Mathf.Abs(jumpGravity));
        this.jumpHeldTimerMax = Mathf.Max(0f, jumpHeldTimerMax);
        this.fallSpeedMax = Mathf.Max(0.01f, fallSpeedMax);
        this.maximumStepHeight = Mathf.Max(0f, maximumStepHeight);
        SetJumpSnapDistance(maximumJumpSnapDistance);
        SetJumpWallAttachDistance(maximumJumpWallAttachDistance);
    }

    internal void SetStepHeight(float height)
    {
        maximumStepHeight = Mathf.Max(0f, height);
    }

    internal void SetJumpSnapDistance(float distance)
    {
        maximumJumpSnapDistance = Mathf.Max(0f, distance);
    }

    internal void SetJumpWallAttachDistance(float distance)
    {
        maximumJumpWallAttachDistance = Mathf.Max(0f, distance);
    }

    internal bool TryGetTeleportPose(Collider destination, Vector3 footPoint,
        Vector3 normal, Vector3 forward, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;
        if (!IsUsableSurface(destination) || IsPlayerCollider(destination)
            || (groundMask.value & (1 << destination.gameObject.layer)) == 0
            || !IsFinite(footPoint) || !IsFinite(normal) || !IsFinite(forward)
            || normal.sqrMagnitude < 0.000001f)
            return false;

        Physics.SyncTransforms();
        normal.Normalize();
        // Check the actual collider face, rather than trusting a marker on a
        // hidden face or a normal which points into the destination block.
        const float faceProbe = 0.05f;
        if (!destination.Raycast(new Ray(footPoint + normal * faceProbe, -normal),
                out RaycastHit landing, faceProbe * 2f)
            || Vector3.Dot(landing.normal, normal) < MinimumFloorAlignment
            || (landing.point - footPoint).sqrMagnitude > 0.0001f)
            return false;

        forward = Vector3.ProjectOnPlane(forward, normal);
        if (forward.sqrMagnitude < 0.000001f)
            forward = Vector3.ProjectOnPlane(player.forward, normal);
        if (forward.sqrMagnitude < 0.000001f)
            forward = Vector3.ProjectOnPlane(player.right, normal);
        rotation = Quaternion.LookRotation(forward.normalized, normal);

        CapsuleDimensions(out float radius, out _, out float halfHeight);
        if (radius <= 0f || !IsFinite(player.lossyScale))
            return false;
        Vector3 center = landing.point + normal * (halfHeight + CastClearance);
        position = center - rotation * Vector3.Scale(controller.center, player.lossyScale);
        Vector3 segment = normal * (halfHeight - radius);
        // A teleport has no sweep path. Validate the whole arrival volume with
        // the full radius so another solid object cannot contain the player.
        int count = Physics.OverlapCapsuleNonAlloc(center + segment, center - segment,
            radius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Collider[] hits = overlaps;
        if (count == hits.Length)
        {
            hits = Physics.OverlapCapsule(center + segment, center - segment,
                radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        for (int i = 0; i < count; i++)
        {
            if (!IsPlayerCollider(hits[i]))
                return false;
        }
        return true;
    }

    internal void BindTeleportDestination(Collider destination, Vector3 footPoint)
    {
        // Clear every old takeoff, transition and support anchor before binding
        // the new floor. Otherwise edge recovery can move back to the old king.
        transitioning = airborneWallTransition = recoveringFromTransition = false;
        jumping = falling = fallFromJump = jumpReleased = steppingOverLedge = false;
        transitionElapsed = transitionHalfSegment = fallVelocity = blockedFallTime = 0f;
        jumpVelocity = jumpHeldTimer = jumpElapsed = stepSettleVelocity = 0f;
        transitionPivotLocal = transitionOffsetLocal = Vector3.zero;
        transitionStartUpLocal = transitionEndUpLocal = Vector3.zero;
        transitionSource = stepSource = null;
        transitionSourceCenterLocal = transitionSourceUpLocal = stepSourcePointLocal = Vector3.zero;
        surface = lastSupportedSurface = destination;
        Transform floor = destination.transform;
        localPlayerPosition = floor.InverseTransformPoint(player.position);
        previousSurfaceRotation = floor.rotation;
        lastSupportedCenterLocal = floor.InverseTransformPoint(CapsuleCenter());
        lastSupportedPointLocal = floor.InverseTransformPoint(footPoint);
        lastSupportedUpLocal = Quaternion.Inverse(floor.rotation) * player.up;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    internal void Step(Vector2 moveInput, float moveSpeed, float deltaTime,
        bool jumpPressed = false, bool jumpHeld = false)
    {
        // CubeLayerRotator changes collider Transforms in its coroutine before LateUpdate.
        Physics.SyncTransforms();

        if (recoveringFromTransition && !RestoreSourceSupport())
            return;

        if (jumping && !HasValidSurface())
        {
            jumping = false;
            AbortSurfaceTransition();
            if (recoveringFromTransition)
                return;
        }

        if (transitioning && !HasValidSurface())
        {
            AbortSurfaceTransition();
            if (recoveringFromTransition)
                return;
        }

        if (!HasValidSurface())
        {
            surface = null;
            steppingOverLedge = false;
            if (!falling && !AcquireSurface())
                return;
        }
        else
        {
            Transform floor = surface.transform;
            Quaternion rotationDelta = floor.rotation * Quaternion.Inverse(previousSurfaceRotation);
            Vector3 carriedPosition = floor.TransformPoint(localPlayerPosition);
            Quaternion carriedRotation = rotationDelta * player.rotation;
            if ((transitioning || jumping || falling) && !IsPoseClear(carriedPosition, carriedRotation))
            {
                bool canRecoverToTakeoff = transitioning || jumping || (falling && fallFromJump);
                jumping = false;
                if (canRecoverToTakeoff)
                {
                    falling = false;
                    AbortSurfaceTransition();
                }
                else
                {
                    // Ordinary falling has no takeoff anchor to return to.
                    // Detach from a blocked carrier and continue from this pose.
                    surface = null;
                }
                return;
            }
            player.SetPositionAndRotation(carriedPosition, carriedRotation);
            Physics.SyncTransforms();
        }

        if (transitioning)
        {
            AdvanceSurfaceTransition(deltaTime);
            return;
        }

        if (falling)
        {
            AdvanceFall(moveInput, moveSpeed, deltaTime);
            return;
        }

        if (jumping)
        {
            AdvanceJump(moveInput, moveSpeed, deltaTime, jumpHeld);
            return;
        }

        bool foundGround = TryFindGround(groundProbeDistance, out RaycastHit ground);
        if (!foundGround || (!IsLandingContact(ground) && !CanSettleStep(ground)))
        {
            // Recover a supported pose instead of remembering a stranded edge
            // pose forever. If that old pose changed, still allow retreat toward
            // a nearby patch of the original floor under collision protection.
            if (!TryRestoreWalkingSupport(out ground))
            {
                TryRetreatToWalkingSupport(moveInput, moveSpeed, deltaTime);
                RememberPosition();
                return;
            }
        }

        if (IsLandingContact(ground) || HasStepContact())
        {
            SnapToGround(ground);
            stepSettleVelocity = 0f;
        }
        else
        {
            SettleStep(deltaTime);
        }
        SetSurface(ground.collider);

        if (jumpPressed && jumpStartingVelocity > 0f && deltaTime > 0f)
        {
            // Retain a supported pose in the floor's moving frame. A missed
            // landing returns here instead of introducing a world-space fall.
            transitionSource = surface;
            transitionSourceCenterLocal = surface.transform.InverseTransformPoint(CapsuleCenter());
            transitionSourceUpLocal = Quaternion.Inverse(surface.transform.rotation) * player.up;
            jumpVelocity = jumpStartingVelocity;
            jumpHeldTimer = jumpElapsed = 0f;
            jumpReleased = false;
            jumping = true;
            steppingOverLedge = false;
            AdvanceJump(moveInput, moveSpeed, deltaTime, jumpHeld);
            return;
        }

        Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
        Vector3 tangent = Vector3.ProjectOnPlane(
            player.right * input.x + player.forward * input.y, player.up);

        if (tangent.sqrMagnitude > 0.000001f)
        {
            Vector3 motion = tangent.normalized * input.magnitude * Mathf.Max(0f, moveSpeed)
                * Mathf.Max(0f, deltaTime);
            AdvanceWalking(ground, motion, deltaTime);
        }

        RememberPosition();
    }

    private void AdvanceWalking(RaycastHit ground, Vector3 motion, float deltaTime)
    {
        CapsuleDimensions(out float radius, out _, out _);
        // Check the path, not only its destination: a fast frame must not skip
        // an unsupported gap just because the far platform is under its feet.
        float stepDistance = Mathf.Max(0.001f, Mathf.Min(MaximumWalkingStepDistance, radius * 0.1f));
        int steps = Mathf.Max(1, Mathf.CeilToInt(motion.magnitude / stepDistance));
        Vector3 stepMotion = motion / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 supportedPosition = player.position;
            // A thin platform's side is a small step before it is a joined wall.
            if (TryStepOverLedge(ground, stepMotion))
            {
                TryFindGround(groundProbeDistance, out ground);
                continue;
            }
            if (allowSurfaceTransitions && stepMotion.sqrMagnitude > 0f
                && TryBeginSurfaceTransition(ground, stepMotion.normalized, stepMotion.magnitude))
            {
                AdvanceSurfaceTransition(deltaTime);
                return;
            }
            MoveCapsule(stepMotion, true);

            if (!TryFindGround(groundProbeDistance, out RaycastHit nextGround)
                || (!IsLandingContact(nextGround) && !CanSettleStep(nextGround)))
            {
                // Finding a lower surface does not permit walking off a ledge.
                player.position = supportedPosition;
                Physics.SyncTransforms();
                break;
            }
            if (IsLandingContact(nextGround) || HasStepContact())
                SnapToGround(nextGround);
            SetSurface(nextGround.collider);
            ground = nextGround;
        }
    }

    private bool HasStepContact()
    {
        if (!steppingOverLedge)
            return false;
        CapsuleDimensions(out float radius, out float queryRadius, out _);
        // A rounded edge may support the capsule while the center ray still
        // sees the old lower floor. This contact can pause step settling.
        return TrySweepCapsule(CapsuleCenter(), player.up, -player.up,
                radius - queryRadius + CastClearance * 2f, out RaycastHit contact)
            && (groundMask.value & (1 << contact.collider.gameObject.layer)) != 0
            && Vector3.Dot(contact.normal, player.up) > 0.01f;
    }

    private bool CanSettleStep(RaycastHit ground)
    {
        if (!steppingOverLedge || !IsUsableSurface(stepSource))
            return false;
        Vector3 sourcePoint = stepSource.transform.TransformPoint(stepSourcePointLocal);
        CapsuleDimensions(out _, out _, out float halfHeight);
        float gap = Vector3.Dot(CapsuleCenter() - ground.point, player.up) - halfHeight;
        return Vector3.Dot(ground.point - sourcePoint, player.up) >= -CastClearance * 2f
            && gap <= maximumStepHeight * Mathf.Abs(player.lossyScale.y) + CastClearance * 2f;
    }

    private void SettleStep(float deltaTime)
    {
        float remainingTime = Mathf.Min(Mathf.Max(0f, deltaTime), 2f);
        while (remainingTime > 0f)
        {
            float stepTime = Mathf.Min(remainingTime, 1f / 60f);
            remainingTime -= stepTime;
            float gravityTime = Mathf.Min(stepTime, Mathf.Max(0f, (stepSettleVelocity + fallSpeedMax) / jumpGravity));
            float descent = stepSettleVelocity * gravityTime - 0.5f * jumpGravity * gravityTime * gravityTime
                - fallSpeedMax * (stepTime - gravityTime);
            stepSettleVelocity = Mathf.Max(-fallSpeedMax, stepSettleVelocity - jumpGravity * stepTime);
            MoveCapsule(player.up * descent, false);
            if (TryFindGround(groundProbeDistance, out RaycastHit ground) && IsLandingContact(ground))
            {
                SnapToGround(ground);
                stepSettleVelocity = 0f;
                break;
            }
        }
    }

    private bool TryRestoreWalkingSupport(out RaycastHit ground)
    {
        ground = default;
        if (!IsUsableSurface(lastSupportedSurface))
            return false;
        Transform source = lastSupportedSurface.transform;
        Vector3 up = source.rotation * lastSupportedUpLocal;
        // This recovery validates translation only. An independently turning
        // old floor must not rotate the capsule through unchecked obstacles.
        if (Vector3.Dot(player.up, up.normalized) < 0.999999f)
            return false;
        Quaternion rotation = player.rotation;
        Vector3 center = source.TransformPoint(lastSupportedCenterLocal);
        Vector3 position = center - rotation * Vector3.Scale(controller.center, player.lossyScale);
        CapsuleDimensions(out _, out _, out float halfHeight);
        Vector3 motion = center - CapsuleCenter();
        if (!IsPoseClear(position, rotation)
            || !TryRaycastSurface(center, -up, halfHeight + CastClearance * 2f, groundMask, out ground)
            || ground.collider != lastSupportedSurface
            || Vector3.Dot(ground.normal, up) < MinimumFloorAlignment
            || (motion.magnitude > 0.0001f && TrySweepCapsule(CapsuleCenter(), player.up,
                motion.normalized, motion.magnitude, out _)))
            return false;
        player.SetPositionAndRotation(position, rotation);
        SetSurface(ground.collider);
        steppingOverLedge = false;
        stepSettleVelocity = 0f;
        Physics.SyncTransforms();
        return true;
    }

    private void TryRetreatToWalkingSupport(Vector2 moveInput, float moveSpeed, float deltaTime)
    {
        if (!IsUsableSurface(lastSupportedSurface))
            return;
        Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
        Vector3 tangent = Vector3.ProjectOnPlane(player.right * input.x + player.forward * input.y, player.up);
        float distance = input.magnitude * Mathf.Max(0f, moveSpeed) * Mathf.Max(0f, deltaTime);
        if (tangent.sqrMagnitude < 0.000001f || distance < 0.0001f)
            return;
        Vector3 direction = tangent.normalized;
        Vector3 sourcePoint = lastSupportedSurface.transform.TransformPoint(lastSupportedPointLocal);
        Vector3 floorPoint = CapsuleCenter() - player.up * Vector3.Dot(CapsuleCenter() - sourcePoint, player.up);
        CapsuleDimensions(out float radius, out _, out _);
        float reach = Mathf.Max(radius, groundProbeDistance) + maximumStepHeight * Mathf.Abs(player.lossyScale.y);
        int probes = Mathf.Max(1, Mathf.CeilToInt(reach / MaximumWalkingStepDistance));
        for (int i = 1; i <= probes; i++)
        {
            float ahead = reach * i / probes;
            Vector3 probe = floorPoint + direction * ahead + player.up * 0.05f;
            if (!TryRaycastSurface(probe, -player.up, 0.05f + CastClearance * 2f,
                    groundMask, out RaycastHit support)
                || support.collider != lastSupportedSurface
                || Vector3.Dot(support.normal, player.up) < MinimumFloorAlignment
                || Mathf.Abs(Vector3.Dot(support.point - sourcePoint, player.up)) > CastClearance * 2f)
                continue;
            MoveCapsule(direction * Mathf.Min(distance, ahead), true);
            Physics.SyncTransforms();
            if (TryFindGround(groundProbeDistance, out RaycastHit ground)
                && (IsLandingContact(ground) || CanSettleStep(ground)))
                SetSurface(ground.collider);
            return;
        }
    }

    private bool HasValidSurface()
    {
        return surface != null && surface.enabled && !surface.isTrigger
            && surface.gameObject.activeInHierarchy;
    }

    private void AdvanceJump(Vector2 moveInput, float moveSpeed, float deltaTime, bool jumpHeld)
    {
        Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
        float remainingTime = Mathf.Min(Mathf.Max(0f, deltaTime), 2f);
        while (remainingTime > 0f && jumping)
        {
            float stepTime = Mathf.Min(remainingTime, 1f / 60f);
            remainingTime -= stepTime;
            jumpElapsed += stepTime;
            if (!jumpHeld)
                jumpReleased = true;

            // Match normal-mode held jumping, with gravity along local -up.
            float heldTime = !jumpReleased && jumpVelocity > 0f
                ? Mathf.Min(stepTime, Mathf.Max(0f, jumpHeldTimerMax - jumpHeldTimer)) : 0f;
            jumpHeldTimer += heldTime;
            float gravityTime = stepTime - heldTime;
            float verticalDistance = jumpVelocity * stepTime
                - 0.5f * jumpGravity * gravityTime * gravityTime;
            jumpVelocity = Mathf.Max(-fallSpeedMax, jumpVelocity - jumpGravity * gravityTime);

            Vector3 origin = transitionSource.transform.TransformPoint(transitionSourceCenterLocal);
            float height = Vector3.Dot(CapsuleCenter() - origin, player.up);
            // Never descend below the starting surface when no new landing exists.
            verticalDistance = Mathf.Max(verticalDistance, -Mathf.Max(0f, height));
            Vector3 before = player.position;
            MoveCapsule(player.up * verticalDistance, false);
            if (verticalDistance > 0f
                && Vector3.Dot(player.position - before, player.up) < verticalDistance - CastClearance)
            {
                jumpVelocity = 0f;
                jumpReleased = true;
            }

            Vector3 tangent = Vector3.ProjectOnPlane(
                player.right * input.x + player.forward * input.y, player.up);
            if (tangent.sqrMagnitude > 0.000001f)
                MoveCapsule(tangent.normalized * input.magnitude * Mathf.Max(0f, moveSpeed) * stepTime, true);

            if (allowSurfaceTransitions && moveSpeed > 0f && tangent.sqrMagnitude > 0.000001f
                && TryBeginJumpWallTransition(tangent.normalized))
            {
                AdvanceSurfaceTransition(stepTime + remainingTime);
                return;
            }

            CapsuleDimensions(out _, out _, out float halfHeight);
            if (jumpVelocity <= 0f && verticalDistance <= 0f
                && TryRaycastSurface(CapsuleCenter(), -player.up,
                    halfHeight + Mathf.Max(0.12f, maximumJumpSnapDistance), groundMask,
                    out RaycastHit landing)
                && Vector3.Dot(landing.normal, player.up) > 0.01f)
            {
                if (IsLandingContact(landing) && TryLandOnSurface(landing))
                    break;
                if (!IsLandingContact(landing))
                {
                    // A detected landing is a destination, not an immediate snap.
                    // Continue downward from the jump's existing velocity, even
                    // when the destination is below the takeoff surface.
                    BeginFall(jumpVelocity);
                    AdvanceFall(moveInput, moveSpeed, remainingTime);
                    return;
                }
            }

            height = Vector3.Dot(CapsuleCenter() - origin, player.up);
            float maximumFlightTime = jumpHeldTimerMax + 2f * jumpStartingVelocity / jumpGravity + 1f;
            if ((jumpVelocity <= 0f && height <= CastClearance) || jumpElapsed > maximumFlightTime)
            {
                jumping = false;
                AbortSurfaceTransition();
                break;
            }
        }
        RememberPosition();
        Physics.SyncTransforms();
    }

    private bool TryLandOnSurface(RaycastHit ground)
    {
        if (!IsLandingContact(ground))
            return false;

        CapsuleDimensions(out float radius, out _, out float halfHeight);
        float halfSegment = halfHeight - radius;
        Vector3 oldCenter = CapsuleCenter();
        Vector3 oldUp = player.up;
        Vector3 newUp = ground.normal.normalized;
        Vector3 lowerCenter = oldCenter - oldUp * halfSegment;
        Vector3 settledLowerCenter = lowerCenter + newUp *
            (radius + CastClearance - Vector3.Dot(lowerCenter - ground.point, newUp));
        Quaternion oldRotation = player.rotation;
        Quaternion endRotation = Quaternion.FromToRotation(oldUp, newUp) * oldRotation;
        Vector3 endCenter = settledLowerCenter + newUp * halfSegment;
        Vector3 previousCenter = oldCenter;
        Vector3 previousUp = oldUp;
        // Keep the lower hemisphere beside the landing plane while aligning the
        // body, and check every collider, including the platform and its end caps.
        for (int sample = 1; sample <= 30; sample++)
        {
            float progress = sample / 30f;
            Quaternion rotation = Quaternion.Slerp(oldRotation, endRotation, progress);
            Vector3 up = rotation * Vector3.up;
            Vector3 center = Vector3.Lerp(lowerCenter, settledLowerCenter, progress) + up * halfSegment;
            Vector3 position = center - rotation * Vector3.Scale(controller.center, player.lossyScale);
            Vector3 motion = center - previousCenter;
            float distance = motion.magnitude;
            // A long landing snap must sweep between poses too: endpoint-only
            // overlap checks could skip a thin obstacle on the way down.
            if (distance > 0.0001f && TrySweepCapsule(previousCenter, previousUp,
                    motion / distance, distance, out _))
                return false;
            if (!IsPoseClear(position, rotation))
                return false;
            previousCenter = center;
            previousUp = up;
        }
        if (!TryRaycastSurface(endCenter, -newUp, halfHeight + groundProbeDistance,
                groundMask, out RaycastHit support) || support.collider != ground.collider
            || Vector3.Dot(support.normal, newUp) < MinimumFloorAlignment)
            return false;

        player.SetPositionAndRotation(endCenter - endRotation *
            Vector3.Scale(controller.center, player.lossyScale), endRotation);
        jumping = false;
        falling = false;
        fallFromJump = false;
        fallVelocity = 0f;
        blockedFallTime = 0f;
        SetSurface(ground.collider);
        RememberPosition();
        return true;
    }

    private bool TryBeginJumpWallTransition(Vector3 direction)
    {
        if (maximumJumpWallAttachDistance <= 0f)
            return false;
        CapsuleDimensions(out float radius, out float queryRadius, out float halfHeight);
        Vector3 center = CapsuleCenter();
        Vector3 oldUp = player.up;
        // Probe the nearest solid in the requested movement direction. An
        // obstacle cannot be skipped to grab a walkable wall behind it.
        if (!TrySweepCapsule(center, oldUp, direction,
                maximumJumpWallAttachDistance + radius - queryRadius + CastClearance,
                out RaycastHit obstacle)
            || (groundMask.value & (1 << obstacle.collider.gameObject.layer)) == 0)
            return false;
        float halfSegment = halfHeight - radius;
        Vector3 lowerCenter = center - oldUp * halfSegment;
        if (!TryRaycastSurface(lowerCenter, direction,
                radius + maximumJumpWallAttachDistance + CastClearance,
                Physics.DefaultRaycastLayers, out RaycastHit wall)
            || wall.collider != obstacle.collider)
            return false;
        Vector3 newUp = wall.normal.normalized;
        float alignment = Vector3.Dot(oldUp, newUp);
        Vector3 approachNormal = Vector3.ProjectOnPlane(newUp, oldUp).normalized;
        // Ordinary ramps still use downward landing. This entry handles walls
        // at 45--135 degrees to the current floor, including overhangs.
        if (alignment > 0.7072f || alignment < -0.7072f
            || Vector3.Dot(direction, -approachNormal) < 0.5f)
            return false;

        Vector3 settledLowerCenter = lowerCenter + newUp * (radius + CastClearance
            - Vector3.Dot(lowerCenter - wall.point, newUp));
        Vector3 foot = settledLowerCenter - newUp * (radius + CastClearance);
        Vector3 wallTangent = Vector3.ProjectOnPlane(oldUp, newUp).normalized;
        // A thin glass rim or bridge end cap is not a climbing wall. Verify a
        // real patch of this same face around the intended foot contact.
        if (!HasWallPatch(foot + wallTangent * radius, newUp, wall.collider)
            || !HasWallPatch(foot - wallTangent * radius, newUp, wall.collider))
            return false;

        Collider oldSurface = surface;
        surface = wall.collider;
        Transform target = surface.transform;
        transitionPivotLocal = target.InverseTransformPoint(settledLowerCenter);
        transitionOffsetLocal = target.InverseTransformVector(lowerCenter - settledLowerCenter);
        transitionStartUpLocal = Quaternion.Inverse(target.rotation) * oldUp;
        transitionEndUpLocal = Quaternion.Inverse(target.rotation) * newUp;
        transitionHalfSegment = halfSegment;
        transitionElapsed = 0f;
        Vector3 previousCenter = center;
        Vector3 previousUp = oldUp;
        for (int sample = 0; sample <= 30; sample++)
        {
            TransitionPose(sample / 30f, out Vector3 position, out Quaternion rotation);
            Vector3 nextCenter = position + rotation * Vector3.Scale(controller.center, player.lossyScale);
            Vector3 motion = nextCenter - previousCenter;
            float distance = motion.magnitude;
            if ((distance > 0.0001f && TrySweepCapsule(previousCenter, previousUp,
                    motion / distance, distance, out _)) || !IsPoseClear(position, rotation))
            {
                surface = oldSurface;
                return false;
            }
            previousCenter = nextCenter;
            previousUp = rotation * Vector3.up;
        }
        TransitionPose(1f, out Vector3 endPosition, out Quaternion endRotation);
        if (!HasTargetSupport(endPosition, endRotation))
        {
            surface = oldSurface;
            return false;
        }
        // Retain the jump's verified takeoff anchor for recovery. An airborne
        // pose cannot replace it, since it has no floor support to return to.
        jumping = false;
        steppingOverLedge = false;
        airborneWallTransition = true;
        transitioning = true;
        RememberPosition();
        return true;
    }

    private bool HasWallPatch(Vector3 point, Vector3 normal, Collider wall)
    {
        return TryRaycastSurface(point + normal * 0.05f, -normal, 0.1f,
                groundMask, out RaycastHit support) && support.collider == wall
            && Vector3.Dot(support.normal, normal) >= MinimumFloorAlignment;
    }

    private bool AcquireSurface()
    {
        if (!TryFindGround(initialSurfaceSearchDistance, out RaycastHit ground))
            return false;

        if (IsLandingContact(ground) && TryLandOnSurface(ground))
            return true;

        // Keep the current pose and do not attach to the detected surface yet.
        BeginFall(0f);
        return true;
    }

    private void BeginFall(float downwardVelocity)
    {
        fallFromJump = jumping;
        falling = true;
        jumping = false;
        fallVelocity = Mathf.Clamp(downwardVelocity, -fallSpeedMax, 0f);
        blockedFallTime = 0f;
        steppingOverLedge = false;
        RememberPosition();
    }

    private bool IsLandingContact(RaycastHit ground)
    {
        CapsuleDimensions(out float radius, out _, out float halfHeight);
        Vector3 lowerCenter = CapsuleCenter() - player.up * (halfHeight - radius);
        float gap = Vector3.Dot(lowerCenter - ground.point, ground.normal.normalized) - radius;
        // Only correct a contact-sized gap. Probe distances must never become
        // instantaneous travel distances, including on an inclined plane.
        return gap <= CastClearance * 2f;
    }

    private bool TryFindLanding(float extraDistance, out RaycastHit landing)
    {
        CapsuleDimensions(out _, out _, out float halfHeight);
        return TryRaycastSurface(CapsuleCenter(), -player.up,
                halfHeight + extraDistance, groundMask, out landing)
            && Vector3.Dot(landing.normal, player.up) > 0.01f;
    }

    private void AdvanceFall(Vector2 moveInput, float moveSpeed, float deltaTime)
    {
        Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
        float remainingTime = Mathf.Min(Mathf.Max(0f, deltaTime), 2f);
        float probeDistance = Mathf.Max(initialSurfaceSearchDistance,
            Mathf.Max(groundProbeDistance, Mathf.Max(0.12f, maximumJumpSnapDistance)));
        while (remainingTime > 0f && falling)
        {
            if (TryFindLanding(probeDistance, out RaycastHit ground)
                && IsLandingContact(ground) && TryLandOnSurface(ground))
                break;

            float stepTime = Mathf.Min(remainingTime, 1f / 60f);
            remainingTime -= stepTime;
            float gravityTime = Mathf.Min(stepTime, Mathf.Max(0f, (fallVelocity + fallSpeedMax) / jumpGravity));
            float verticalDistance = fallVelocity * gravityTime - 0.5f * jumpGravity * gravityTime * gravityTime
                - fallSpeedMax * (stepTime - gravityTime);
            fallVelocity = Mathf.Max(-fallSpeedMax, fallVelocity - jumpGravity * stepTime);
            // Sweep the whole capsule, so a thin obstacle cannot be crossed even
            // when a long downward probe sees another surface behind it.
            Vector3 beforeFall = player.position;
            MoveCapsule(player.up * verticalDistance, false);

            Vector3 tangent = Vector3.ProjectOnPlane(
                player.right * input.x + player.forward * input.y, player.up);
            if (tangent.sqrMagnitude > 0.000001f)
                MoveCapsule(tangent.normalized * input.magnitude * Mathf.Max(0f, moveSpeed) * stepTime, true);

            if (fallFromJump && allowSurfaceTransitions && IsUsableSurface(transitionSource)
                && moveSpeed > 0f && tangent.sqrMagnitude > 0.000001f
                && TryBeginJumpWallTransition(tangent.normalized))
            {
                falling = false;
                fallVelocity = 0f;
                AdvanceSurfaceTransition(stepTime + remainingTime);
                return;
            }

            if (TryFindLanding(probeDistance, out ground)
                && IsLandingContact(ground) && TryLandOnSurface(ground))
                break;

            // Retain the old jump recovery only when collision blocks descent
            // and a landing pose cannot fit. A normal, unobstructed fall has no
            // flight timeout and can reach a surface below its takeoff height.
            if (fallFromJump && verticalDistance < -CastClearance
                && Vector3.Dot(beforeFall - player.position, player.up) < CastClearance)
            {
                blockedFallTime += stepTime;
                if (blockedFallTime >= BlockedFallRecoveryDelay && IsUsableSurface(transitionSource))
                {
                    falling = false;
                    AbortSurfaceTransition();
                    break;
                }
            }
            else
            {
                blockedFallTime = 0f;
            }
        }
        RememberPosition();
        Physics.SyncTransforms();
    }

    private void SetSurface(Collider nextSurface)
    {
        // Use the actual floor collider, whose Transform survives the cube's
        // temporary reparenting to RotationPivot and back to CubeParent.
        surface = nextSurface;
    }

    private void RememberPosition()
    {
        if (!HasValidSurface())
            return;

        Transform floor = surface.transform;
        localPlayerPosition = floor.InverseTransformPoint(player.position);
        previousSurfaceRotation = floor.rotation;
        if (!transitioning && !jumping && !falling
            && TryFindGround(groundProbeDistance, out RaycastHit ground)
            && IsLandingContact(ground) && IsPoseClear(player.position, player.rotation))
        {
            lastSupportedSurface = ground.collider;
            Transform support = ground.collider.transform;
            lastSupportedCenterLocal = support.InverseTransformPoint(CapsuleCenter());
            lastSupportedPointLocal = support.InverseTransformPoint(ground.point);
            lastSupportedUpLocal = Quaternion.Inverse(support.rotation) * player.up;
        }
    }

    private Vector3 CapsuleCenter()
    {
        return player.TransformPoint(controller.center);
    }

    private void CapsuleGeometry(out Vector3 upper, out Vector3 lower,
        out float queryRadius, out float halfHeight)
    {
        CapsuleDimensions(out float radius, out queryRadius, out halfHeight);
        Vector3 center = CapsuleCenter();
        Vector3 offset = player.up * (halfHeight - radius);
        upper = center + offset;
        lower = center - offset;
    }

    private void CapsuleDimensions(out float radius, out float queryRadius, out float halfHeight)
    {
        Vector3 scale = player.lossyScale;
        radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        halfHeight = Mathf.Max(controller.height * Mathf.Abs(scale.y) * 0.5f, radius);
        float skin = controller.skinWidth * Mathf.Max(Mathf.Abs(scale.x),
            Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        queryRadius = Mathf.Max(0.001f, radius - Mathf.Min(skin, radius * 0.45f));

    }

    private bool TryBeginSurfaceTransition(RaycastHit ground, Vector3 direction, float moveDistance)
    {
        CapsuleDimensions(out float radius, out _, out float halfHeight);
        Vector3 feet = CapsuleCenter() - player.up * halfHeight;
        // A low probe requires a wall joined to the floor. An overhead obstacle
        // can block the capsule without becoming the next walking surface.
        Vector3 probeOrigin = feet + player.up * Mathf.Max(0.05f, radius * 0.2f);
        float reach = radius + surfaceTransitionReach + Mathf.Min(moveDistance, radius);
        if (!TryRaycastSurface(probeOrigin, direction, reach, groundMask, out RaycastHit wall))
            return false;

        float alignment = Vector3.Dot(player.up, wall.normal);
        Vector3 approachNormal = Vector3.ProjectOnPlane(wall.normal, player.up).normalized;
        if (alignment < -0.001f || alignment > MinimumFloorAlignment
            || Vector3.Dot(direction, -approachNormal) < 0.5f)
            return false;

        Vector3 oldUp = player.up;
        Vector3 newUp = wall.normal.normalized;
        float halfSegment = halfHeight - radius;
        Vector3 lowerCenter = CapsuleCenter() - oldUp * halfSegment;
        float clearance = radius + CastClearance;
        float floorCorrection = clearance - Vector3.Dot(lowerCenter - ground.point, oldUp);
        float wallCorrection = clearance - Vector3.Dot(lowerCenter - wall.point, newUp);
        float denominator = 1f - alignment * alignment;
        Vector3 pivot = lowerCenter
            + oldUp * ((floorCorrection - alignment * wallCorrection) / denominator)
            + newUp * ((wallCorrection - alignment * floorCorrection) / denominator);

        // A nearby wall across a gap is not joined to this floor. Verify floor
        // support all the way to the two planes' seam before starting a turn.
        Vector3 seam = pivot - (oldUp + newUp) * (clearance / (1f + alignment));
        Vector3 floorEnd = seam + approachNormal * CastClearance * 2f;
        if (!HasJoinedFloorSupport(ground, floorEnd))
            return false;

        Collider oldSurface = surface;
        transitionSource = oldSurface;
        transitionSourceCenterLocal = oldSurface.transform.InverseTransformPoint(CapsuleCenter());
        transitionSourceUpLocal = Quaternion.Inverse(oldSurface.transform.rotation) * oldUp;
        surface = wall.collider;
        Transform target = surface.transform;
        transitionPivotLocal = target.InverseTransformPoint(pivot);
        transitionOffsetLocal = target.InverseTransformVector(CapsuleCenter() - pivot - oldUp * halfSegment);
        transitionStartUpLocal = Quaternion.Inverse(target.rotation) * oldUp;
        transitionEndUpLocal = Quaternion.Inverse(target.rotation) * newUp;
        transitionHalfSegment = halfSegment;
        transitionElapsed = 0f;
        airborneWallTransition = false;

        // Check the whole arc before committing, including its destination.
        // The lower capsule hemisphere stays beside both joined planes.
        for (int sample = 0; sample <= 30; sample++)
        {
            TransitionPose(sample / 30f, out Vector3 position, out Quaternion rotation);
            if (!IsPoseClear(position, rotation))
            {
                surface = oldSurface;
                return false;
            }
        }
        TransitionPose(1f, out Vector3 endPosition, out Quaternion endRotation);
        if (!HasTargetSupport(endPosition, endRotation))
        {
            surface = oldSurface;
            return false;
        }

        transitioning = true;
        RememberPosition();
        return true;
    }

    private bool HasJoinedFloorSupport(RaycastHit ground, Vector3 end)
    {
        Vector3 up = player.up;
        Vector3 start = CapsuleCenter() - up * Vector3.Dot(CapsuleCenter() - ground.point, up);
        Vector3 path = Vector3.ProjectOnPlane(end - start, up);
        int samples = Mathf.Max(1, Mathf.CeilToInt(path.magnitude / MaximumWalkingStepDistance));
        const float probeHeight = 0.05f;
        for (int i = 1; i <= samples; i++)
        {
            Vector3 point = start + path * (i / (float)samples);
            if (!TryRaycastSurface(point + up * probeHeight, -up,
                    probeHeight + CastClearance * 2f, groundMask, out RaycastHit support)
                || Vector3.Dot(support.normal, up) < MinimumFloorAlignment
                || Mathf.Abs(Vector3.Dot(support.point - ground.point, up)) > CastClearance * 2f)
                return false;
        }
        return true;
    }

    private void AdvanceSurfaceTransition(float deltaTime)
    {
        float nextElapsed = Mathf.Min(surfaceTransitionDuration,
            transitionElapsed + Mathf.Max(0f, deltaTime));
        float oldProgress = transitionElapsed / surfaceTransitionDuration;
        float newProgress = nextElapsed / surfaceTransitionDuration;
        // Rotation needs separate volume checks: a translation cast alone does
        // not cover the swept capsule when its up direction changes.
        int samples = Mathf.Max(1, Mathf.CeilToInt((newProgress - oldProgress) * 60f));
        for (int sample = 1; sample <= samples; sample++)
        {
            float progress = Mathf.Lerp(oldProgress, newProgress, sample / (float)samples);
            TransitionPose(progress, out Vector3 position, out Quaternion rotation);
            Vector3 nextCenter = position + rotation * Vector3.Scale(controller.center, player.lossyScale);
            Vector3 motion = nextCenter - CapsuleCenter();
            float distance = motion.magnitude;
            if ((airborneWallTransition && distance > 0.0001f
                    && TrySweepCapsule(CapsuleCenter(), player.up, motion / distance, distance, out _))
                || !IsPoseClear(position, rotation)
                || (progress >= 1f && !HasTargetSupport(position, rotation)))
            {
                AbortSurfaceTransition();
                return;
            }
            player.SetPositionAndRotation(position, rotation);
        }
        transitionElapsed = nextElapsed;
        if (nextElapsed >= surfaceTransitionDuration)
        {
            transitioning = false;
            airborneWallTransition = false;
        }
        RememberPosition();
        Physics.SyncTransforms();
    }

    private void AbortSurfaceTransition()
    {
        transitioning = false;
        airborneWallTransition = false;
        surface = null;
        recoveringFromTransition = true;
        RestoreSourceSupport();
        Physics.SyncTransforms();
    }

    private bool RestoreSourceSupport()
    {
        if (!IsUsableSurface(transitionSource))
        {
            // If both joined surfaces disappear, fall back to the usual safe
            // acquisition path instead of following a destroyed transform.
            recoveringFromTransition = false;
            return true;
        }

        Transform source = transitionSource.transform;
        Vector3 up = source.rotation * transitionSourceUpLocal;
        Quaternion rotation = Quaternion.FromToRotation(player.up, up) * player.rotation;
        Vector3 center = source.TransformPoint(transitionSourceCenterLocal);
        Vector3 position = center - rotation * Vector3.Scale(controller.center, player.lossyScale);
        CapsuleDimensions(out _, out _, out float halfHeight);
        if (!IsPoseClear(position, rotation)
            || !TryRaycastSurface(center, -up, halfHeight + groundProbeDistance,
                groundMask, out RaycastHit hit)
            || hit.collider != transitionSource || Vector3.Dot(hit.normal, up) < MinimumFloorAlignment)
            return false;

        // Return to a verified supported pose; a half-tilted capsule cannot
        // reacquire either plane with the ordinary ground alignment threshold.
        player.SetPositionAndRotation(position, rotation);
        surface = transitionSource;
        recoveringFromTransition = false;
        RememberPosition();
        return true;
    }

    private bool IsUsableSurface(Collider collider)
    {
        return collider != null && collider.enabled && !collider.isTrigger
            && collider.gameObject.activeInHierarchy;
    }

    private void TransitionPose(float progress, out Vector3 position, out Quaternion rotation)
    {
        Transform target = surface.transform;
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        Vector3 startUp = target.rotation * transitionStartUpLocal;
        Vector3 endUp = target.rotation * transitionEndUpLocal;
        Quaternion tilt = Quaternion.Slerp(Quaternion.identity,
            Quaternion.FromToRotation(startUp, endUp), eased);
        Vector3 up = tilt * startUp;
        Vector3 center = target.TransformPoint(transitionPivotLocal)
            + up * transitionHalfSegment
            + target.TransformVector(transitionOffsetLocal) * (1f - eased);
        // Incremental tilt retains the player's mouse/stick yaw during a turn.
        rotation = Quaternion.FromToRotation(player.up, up) * player.rotation;
        position = center - rotation * Vector3.Scale(controller.center, player.lossyScale);
    }

    private bool HasTargetSupport(Vector3 position, Quaternion rotation)
    {
        CapsuleDimensions(out _, out _, out float halfHeight);
        Vector3 center = position + rotation * Vector3.Scale(controller.center, player.lossyScale);
        Vector3 up = rotation * Vector3.up;
        return TryRaycastSurface(center, -up, halfHeight + groundProbeDistance,
            groundMask, out RaycastHit hit) && hit.collider == surface
            && Vector3.Dot(hit.normal, up) >= MinimumFloorAlignment;
    }

    private bool IsPoseClear(Vector3 position, Quaternion rotation)
    {
        CapsuleDimensions(out float radius, out float queryRadius, out float halfHeight);
        Vector3 center = position + rotation * Vector3.Scale(controller.center, player.lossyScale);
        Vector3 offset = rotation * Vector3.up * (halfHeight - radius);
        int count = Physics.OverlapCapsuleNonAlloc(center + offset, center - offset,
            queryRadius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Collider[] hits = overlaps;
        if (count == hits.Length)
        {
            hits = Physics.OverlapCapsule(center + offset, center - offset,
                queryRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        for (int i = 0; i < count; i++)
        {
            if (!IsPlayerCollider(hits[i]))
                return false;
        }
        return true;
    }

    private bool TryRaycastSurface(Vector3 origin, Vector3 direction, float distance,
        LayerMask mask, out RaycastHit result)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, castHits,
            distance, mask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = castHits;
        if (count == hits.Length)
        {
            hits = Physics.RaycastAll(origin, direction, distance, mask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        result = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (IsPlayerCollider(hit.collider) || hit.distance >= nearestDistance)
                continue;
            nearestDistance = hit.distance;
            result = hit;
        }
        return result.collider != null;
    }

    private bool TryFindGround(float extraDistance, out RaycastHit ground)
    {
        CapsuleGeometry(out _, out _, out _, out float halfHeight);
        if (steppingOverLedge)
            extraDistance = Mathf.Max(extraDistance,
                maximumStepHeight * Mathf.Abs(player.lossyScale.y) + CastClearance * 2f);
        return TryRaycastSurface(CapsuleCenter(), -player.up,
            halfHeight + extraDistance, groundMask, out ground)
            && Vector3.Dot(ground.normal, player.up) >= MinimumFloorAlignment;
    }

    private void SnapToGround(RaycastHit ground)
    {
        CapsuleGeometry(out _, out _, out _, out float halfHeight);
        float gap = Vector3.Dot(CapsuleCenter() - ground.point, player.up) - halfHeight;
        if (Mathf.Abs(gap) > 0.0001f)
            MoveCapsule(-player.up * gap, false);
        if (Mathf.Abs(Vector3.Dot(CapsuleCenter() - ground.point, player.up) - halfHeight) < CastClearance * 2f)
            steppingOverLedge = false;
    }

    private bool TryStepOverLedge(RaycastHit ground, Vector3 motion)
    {
        float stepHeight = maximumStepHeight * Mathf.Abs(player.lossyScale.y);
        float distance = motion.magnitude;
        if (stepHeight <= 0f || distance < 0.0001f)
            return false;

        Vector3 up = player.up;
        Vector3 center = CapsuleCenter();
        Vector3 direction = motion / distance;
        if (!TrySweepCapsule(center, up, direction, distance + CastClearance, out _))
            return false;

        CapsuleDimensions(out float radius, out float queryRadius, out float halfHeight);
        Vector3 floorPoint = center - up * Vector3.Dot(center - ground.point, up);
        // The capsule meets an edge before its center crosses it. Look under the
        // leading foot, but only move the horizontal distance requested this frame.
        Vector3 probe = floorPoint + direction * (radius + distance + CastClearance)
            + up * (stepHeight + CastClearance * 2f);
        if (!TryRaycastSurface(probe, -up, stepHeight + CastClearance * 4f,
                groundMask, out RaycastHit top)
            || Vector3.Dot(top.normal, up) < MinimumFloorAlignment)
            return false;
        float rise = Vector3.Dot(top.point - floorPoint, up);
        if (rise <= CastClearance || rise > stepHeight + CastClearance)
            return false;

        float lift = Mathf.Max(0f, Vector3.Dot(top.point - (center - up * halfHeight), up))
            + CastClearance * 2f;
        if (TrySweepCapsule(center, up, up, lift + CastClearance, out _))
            return false;
        Vector3 raisedCenter = center + up * lift;
        if (TrySweepCapsule(raisedCenter, up, direction, distance + CastClearance, out _))
            return false;
        Vector3 advancedCenter = raisedCenter + motion;
        if (!TrySweepCapsule(advancedCenter, up, -up, lift + stepHeight + CastClearance,
                out RaycastHit contact)
            || (groundMask.value & (1 << contact.collider.gameObject.layer)) == 0
            || Vector3.Dot(contact.normal, up) <= 0.01f)
            return false;

        // A downward capsule sweep also supports the intermediate pose at the
        // rounded edge while the feet's center still lies over the original floor.
        float descent = Mathf.Max(0f, contact.distance - (radius - queryRadius) - CastClearance);
        Vector3 settledCenter = advancedCenter - up * descent;
        if (Vector3.Dot(settledCenter - center, up) > stepHeight + CastClearance * 2f
            || !TryRaycastSurface(settledCenter, -up, halfHeight + stepHeight + CastClearance * 2f,
                groundMask, out RaycastHit support)
            || Vector3.Dot(support.normal, up) < MinimumFloorAlignment
            // A lower base visible through a gap cannot support a step up.
            || Vector3.Dot(support.point - ground.point, up) < -CastClearance * 2f)
            return false;
        float supportGap = Vector3.Dot(settledCenter - support.point, up) - halfHeight;
        if (supportGap < -CastClearance * 2f || supportGap > stepHeight + CastClearance * 2f)
            return false;
        Vector3 position = player.position + settledCenter - center;
        if (!IsPoseClear(position, player.rotation))
            return false;

        player.position = position;
        if (!steppingOverLedge)
        {
            stepSource = ground.collider;
            stepSourcePointLocal = ground.collider.transform.InverseTransformPoint(ground.point);
        }
        stepSettleVelocity = 0f;
        SetSurface(support.collider);
        steppingOverLedge = supportGap > CastClearance * 2f;
        Physics.SyncTransforms();
        return true;
    }

    private bool TrySweepCapsule(Vector3 center, Vector3 up, Vector3 direction,
        float distance, out RaycastHit nearestHit)
    {
        CapsuleDimensions(out float radius, out float queryRadius, out float halfHeight);
        Vector3 offset = up * (halfHeight - radius);
        int count = Physics.CapsuleCastNonAlloc(center + offset, center - offset, queryRadius,
            direction, castHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = castHits;
        if (count == hits.Length)
        {
            hits = Physics.CapsuleCastAll(center + offset, center - offset, queryRadius,
                direction, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (IsPlayerCollider(hit.collider) || hit.distance >= nearestDistance)
                continue;
            nearestDistance = hit.distance;
            nearestHit = hit;
        }
        return nearestHit.collider != null;
    }

    private void MoveCapsule(Vector3 motion, bool slide)
    {
        Vector3 remaining = motion;
        int iterations = slide ? SlideIterations : 1;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            float distance = remaining.magnitude;
            if (distance < 0.0001f)
                break;

            Vector3 direction = remaining / distance;
            CapsuleGeometry(out Vector3 upper, out Vector3 lower, out float radius, out _);
            int count = Physics.CapsuleCastNonAlloc(upper, lower, radius, direction,
                castHits, distance + CastClearance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = castHits;
            if (count == hits.Length)
            {
                hits = Physics.CapsuleCastAll(upper, lower, radius, direction,
                    distance + CastClearance, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            bool blocked = false;
            RaycastHit nearestHit = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (IsPlayerCollider(hit.collider) || hit.distance >= nearestDistance)
                    continue;

                blocked = true;
                nearestDistance = hit.distance;
                nearestHit = hit;
            }

            if (!blocked)
            {
                player.position += remaining;
                break;
            }

            float travel = Mathf.Clamp(nearestHit.distance - CastClearance, 0f, distance);
            player.position += direction * travel;
            if (!slide)
                break;

            remaining -= direction * travel;
            remaining = Vector3.ProjectOnPlane(remaining, nearestHit.normal);
            remaining = Vector3.ProjectOnPlane(remaining, player.up);
        }
    }

    private bool IsPlayerCollider(Collider collider)
    {
        return collider == null || collider.transform == player
            || collider.transform.IsChildOf(player);
    }
}
