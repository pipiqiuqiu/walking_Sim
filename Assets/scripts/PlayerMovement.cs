using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(Controls))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Surface attachment (walking sim)")]
    [Tooltip("Follow the current surface's movement/rotation. Enabled jumps use its local up direction.")]
    [SerializeField] private bool stickToSurface;
    [Tooltip("How far below the feet to detect a surface before falling onto it under local gravity.")]
    [SerializeField, Min(0.01f)] private float initialSurfaceSearchDistance = 6f;
    [Tooltip("Distance below the feet to check walking support. Walking blocks at unsupported edges; detecting a lower surface does not permit walking off the current platform.")]
    [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.4f;
    [Tooltip("Walk onto joined slopes or walls, and attach to nearby walls when jumping toward them. Open ledges still block walking.")]
    [SerializeField] private bool allowSurfaceTransitions = true;
    [Tooltip("Seconds for the body and camera to turn onto the next surface.")]
    [SerializeField, Min(0.05f)] private float surfaceTransitionDuration = 0.4f;
    [Tooltip("Extra distance ahead of the capsule used to find a joined wall.")]
    [SerializeField, Min(0f)] private float surfaceTransitionReach = 0.3f;
    [Tooltip("Maximum small step height along the player's local up. Zero disables automatic stepping.")]
    [SerializeField, Min(0f)] private float surfaceStepHeight = 0.4f;
    [InspectorName("Surface Landing Detection Distance")]
    [Tooltip("Extra distance below the feet along local down to detect a landing during a descending jump. The player falls onto the detected surface under gravity instead of snapping. In world units; zero keeps contact-only detection.")]
    [SerializeField, Min(0f)] private float surfaceJumpSnapDistance = 2f;
    [Tooltip("Extra distance beyond the capsule for attaching to walls at 45-135 degrees from the current floor while jumping toward them. In world units; zero disables airborne wall attachment.")]
    [SerializeField, Min(0f)] private float surfaceJumpWallAttachDistance = 2f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed;
    [Tooltip("Layers used for ground checks and surface attachment.")]
    [SerializeField] private LayerMask jumpableMask;

    [Header("Gravity and jumping")]
    [SerializeField] private bool canJump;
    [SerializeField] private float jumpStartingVelocity;
    [SerializeField] private float gravity;
    [SerializeField] private float fallSpeedMax;
    [SerializeField] private float jumpHeldTimerMax;
    [SerializeField] private float jumpPreloadTimerMax;
    [SerializeField] private float coyoteTimerMax;
    private float jumpHeldTimer;
    private float jumpPreloadTimer;
    private float coyoteTimer;
    private bool jumping;
    private bool isGrounded;
    private bool wasGroundedLastFrame;
    private Vector3 velocity;
    private Vector3 velocityInput;
    private Vector3 velocityPhysics;
    private CharacterController controller;
    private Controls controls;
    private SurfaceAttachmentMotor surfaceMotor;
    private Rigidbody playerBody;
    private bool controllerWasEnabled;
    private bool bodyUsedGravity;
    private bool bodyWasKinematic;

    
    
    void Start()
    {
        //get the character controller and controls components
        controller = GetComponent<CharacterController>();
        controls = GetComponent<Controls>();
        playerBody = GetComponent<Rigidbody>();
        if (surfaceMotor == null)
            ConfigureMovementMode();
    }

    /// <summary>
    /// Checks a solid face and arrival volume without moving the player or
    /// changing movement mode. Also works before Start and in edit mode.
    /// </summary>
    public bool CanTeleportToSurface(Collider destinationSurface, Vector3 worldFootPoint,
        Vector3 worldNormal, Vector3 worldForward)
    {
        CharacterController candidateController = controller != null
            ? controller : GetComponent<CharacterController>();
        if (candidateController == null)
            return false;
        SurfaceAttachmentMotor candidateMotor = surfaceMotor ?? CreateSurfaceMotor(candidateController);
        return candidateMotor.TryGetTeleportPose(destinationSurface, worldFootPoint,
            worldNormal, worldForward, out _, out _);
    }

    /// <summary>
    /// Places the player's feet on a verified solid face and follows that face's
    /// movement/rotation. Returns false without moving when the arrival is unsafe.
    /// </summary>
    public bool TeleportToSurface(Collider destinationSurface, Vector3 worldFootPoint,
        Vector3 worldNormal, Vector3 worldForward)
    {
        if (!isActiveAndEnabled)
            return false;
        if (controller == null)
            controller = GetComponent<CharacterController>();
        if (controls == null)
            controls = GetComponent<Controls>();
        if (playerBody == null)
            playerBody = GetComponent<Rigidbody>();
        if (controller == null)
            return false;

        SurfaceAttachmentMotor destinationMotor = surfaceMotor ?? CreateSurfaceMotor();
        if (!destinationMotor.TryGetTeleportPose(destinationSurface, worldFootPoint,
                worldNormal, worldForward, out Vector3 position, out Quaternion rotation))
            return false;

        if (playerBody != null && !playerBody.isKinematic)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }
        if (surfaceMotor == null)
        {
            // Standing on arbitrary cube faces requires local gravity and the
            // oriented capsule motor, rather than the world-Y controller mode.
            stickToSurface = true;
            ConfigureMovementMode();
        }

        // The surface motor owns collision movement and keeps the regular
        // CharacterController disabled, including after a portal arrival.
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (playerBody != null)
        {
            playerBody.position = position;
            playerBody.rotation = rotation;
        }
        velocity = velocityInput = velocityPhysics = Vector3.zero;
        jumping = false;
        isGrounded = wasGroundedLastFrame = true;
        jumpHeldTimer = jumpPreloadTimer = coyoteTimer = 0f;
        surfaceMotor.BindTeleportDestination(destinationSurface, worldFootPoint);
        Physics.SyncTransforms();
        return true;
    }

    void Update()
    {
        if (stickToSurface != (surfaceMotor != null)) {
            ConfigureMovementMode();
        }
        if (surfaceMotor != null) {
            return;
        }
        //
        if (controls.JumpTriggered()) //if jump button is pressed
        {
            if (isGrounded) { //regular jump
                BeginJump();
            }
            else if (coyoteTimer > 0) { //coyote time jump
                BeginJump();
            }
            else { //if you are not grounded and didn't coyote jump, start the jump preload timer
                jumpPreloadTimer = jumpPreloadTimerMax;
            }
        }
        
        //lower timers at the end of each frame
        jumpPreloadTimer -= Time.deltaTime;
        coyoteTimer -= Time.deltaTime;
    }
    
    void FixedUpdate()
    {
        if (stickToSurface) {
            return;
        }
        // --isGrounded logic--
        isGrounded = RaycastTouchesGround();
        if (isGrounded && !wasGroundedLastFrame) {
            GroundEnter();
        }
        if (!isGrounded && wasGroundedLastFrame) {
            GroundExit();
        }
        wasGroundedLastFrame = isGrounded;
        if (isGrounded && velocityPhysics.y <= 0) {
            velocityPhysics.y = 0;
        }

        // --gravity logic-- only apply gravity if you are not jumping or if you are jumping but the jump button is not being held down
        if (jumping && jumpHeldTimer < jumpHeldTimerMax) {
            if (controls.JumpHeld()) {
                jumpHeldTimer += Time.fixedDeltaTime;
            }
            else {
                jumpHeldTimer = jumpHeldTimerMax;
            }
        }
        else {
            ApplyGravity();
        }

        // --movement logic--
        Vector2 moveInput = Vector2.ClampMagnitude(controls.MoveInput(), 1f); //get move input vector and clamp to 1
        velocityInput = transform.right * moveInput.x + transform.forward * moveInput.y; //get input velocity
  
        velocityInput *= moveSpeed; //scale by move speed
        
        velocity = velocityInput + velocityPhysics; //combine input velocity and physics velocity
        
        controller.Move(velocity * Time.fixedDeltaTime); //move the player based on the combined velocity
    }

    void LateUpdate()
    {
        // CubeLayerRotator moves pieces in a coroutine, after Update.
        if (surfaceMotor == null)
            return;
        surfaceMotor.SetStepHeight(surfaceStepHeight);
        surfaceMotor.SetJumpSnapDistance(surfaceJumpSnapDistance);
        surfaceMotor.SetJumpWallAttachDistance(surfaceJumpWallAttachDistance);
        surfaceMotor.Step(controls.MoveInput(), moveSpeed, Time.deltaTime,
            canJump && controls.JumpTriggered(), canJump && controls.JumpHeld());
    }

    void ConfigureMovementMode()
    {
        if (!stickToSurface) {
            RestoreNormalMovement();
            return;
        }

        controllerWasEnabled = controller.enabled;
        if (playerBody != null) {
            bodyUsedGravity = playerBody.useGravity;
            bodyWasKinematic = playerBody.isKinematic;
            playerBody.useGravity = false;
            playerBody.isKinematic = true;
        }

        surfaceMotor = CreateSurfaceMotor();
        // The attached motor uses capsule sweeps oriented to the player's local up.
        controller.enabled = false;
        velocityPhysics = Vector3.zero;
        jumping = false;
        isGrounded = false;
        wasGroundedLastFrame = false;
        jumpHeldTimer = jumpPreloadTimer = coyoteTimer = 0f;
    }

    SurfaceAttachmentMotor CreateSurfaceMotor(CharacterController sourceController = null)
    {
        return new SurfaceAttachmentMotor(sourceController != null ? sourceController : controller, jumpableMask,
            initialSurfaceSearchDistance, groundProbeDistance, allowSurfaceTransitions,
            surfaceTransitionDuration, surfaceTransitionReach, jumpStartingVelocity,
            gravity, jumpHeldTimerMax, fallSpeedMax, surfaceStepHeight, surfaceJumpSnapDistance,
            surfaceJumpWallAttachDistance);
    }

    void RestoreNormalMovement()
    {
        if (surfaceMotor == null) {
            return;
        }

        surfaceMotor = null;
        controller.enabled = controllerWasEnabled;
        if (playerBody != null) {
            playerBody.useGravity = bodyUsedGravity;
            playerBody.isKinematic = bodyWasKinematic;
        }
    }

    void OnDisable()
    {
        RestoreNormalMovement();
    }

    void ApplyGravity(float gravityMultiplier = 1f)
    {
        velocityPhysics.y -= gravity * gravityMultiplier * Time.fixedDeltaTime; //apply gravity to the physics y velocity
        if (velocityPhysics.y < -fallSpeedMax) { //make sure fall speed never exceeds fallSpeedMax
            velocityPhysics.y = -fallSpeedMax;
        }
        if (isGrounded && velocityPhysics.y < 0) { //if grounded, reset y velocity to 0
            velocityPhysics.y = 0;
        }
    }

    void BeginJump()
    {
        //if you can't jump, don't do anything
        if (!canJump) {
            return;
        }
        
        //if jumping, set the physics y velocity to the jump starting velocity, reset timers, and set jumping to true
        velocityPhysics.y = jumpStartingVelocity;
        jumpHeldTimer = 0;
        coyoteTimer = 0;
        jumpPreloadTimer = 0;
        jumping = true;
    }
    
    void EndJump(){
        jumping = false;
    }
    
    /// <summary>
    /// Called when you first start touching the ground
    /// </summary>
    void GroundEnter() {
        //if you are jumping and you touch the ground, end the jump
        if (jumping) {
            EndJump();
        }
        //if I just landed on the platform right after pressing the jump button, let me jump
        if (jumpPreloadTimer > 0) {
            BeginJump();
        }
    }

    /// <summary>
    /// Called each frame you are touching the ground
    /// </summary>
    void GroundStay(RaycastHit hit)
    {
        //USE THIS IF YOU WANT SOMETHING TO HAPPEN EACH FRAME YOU ARE TOUCHING THE GROUND
    }

    /// <summary>
    /// Called when you first stop touching the ground
    /// </summary>
    void GroundExit() {
        if (!jumping) {
            coyoteTimer = coyoteTimerMax; //start the coyote timer if you leave the ground and are not jumping
        }
    }
    
    /// <summary>
    /// Cast raycasts from the middle of the player and from 8 corners to test if the player is on the ground
    /// </summary>
    bool RaycastTouchesGround() {
        float rayLength = controller.height * .6f;

        //test if the middle of the player is touching the ground
        if (RaycastTest(transform.position, rayLength)) {
            return true;
        }

        //test if any of the 8 corners of the player is touching the ground
        float halfWidth = transform.localScale.x * .5f;
        float diagonalWidth = transform.localScale.x * .35f;
        
        //test the 4 side of the player
        if (RaycastTest(transform.position + (transform.right * halfWidth), rayLength)) {
            return true;
        }
        if (RaycastTest(transform.position + (-transform.right * halfWidth), rayLength)) {
            return true;
        }
        if (RaycastTest(transform.position + (transform.forward * halfWidth), rayLength)) {
            return true;
        }
        if (RaycastTest(transform.position + (-transform.forward * halfWidth), rayLength)) {
            return true;
        }
        //test the 4 corners of the player
        if (RaycastTest(transform.position + (transform.right * diagonalWidth) + (transform.forward * diagonalWidth), rayLength)) {
            return true;
        }
        if (RaycastTest(transform.position + (transform.right * diagonalWidth) + (-transform.forward * diagonalWidth), rayLength)) {
            return true;
        }
        if (RaycastTest(transform.position + (-transform.right * diagonalWidth) + (transform.forward * diagonalWidth), rayLength)) {
            return true;
        }
        if (RaycastTest(transform.position + (-transform.right * diagonalWidth) + (-transform.forward * diagonalWidth), rayLength)) {
            return true;
        }
        
        //call leave ground results if we were grounded and now we're not
        if (isGrounded) { 
            GroundExit();
        }

        //if we are not touching the ground, return false
        return false;
    }

    /// <summary>
    /// Test a single raycast to see if it hits the ground
    /// </summary>
    /// <param name="startingPoint"> Where the ray begins (it will cast down from here)</param>
    /// <param name="rayLength">How long the ray casts</param>
    bool RaycastTest(Vector3 startingPoint, float rayLength) {
        //cast a ray down from the starting point and check if it hits the ground
        RaycastHit hit;
        if (Physics.Raycast(startingPoint, transform.TransformDirection(Vector3.down), out hit, rayLength, jumpableMask, QueryTriggerInteraction.Ignore)) {
            //if it hits the ground, call the GroundStay function and return true
            GroundStay(hit);
            return true;
        }
        else {
            //if it doesn't hit the ground, return false
            return false;
        }
    }
}
