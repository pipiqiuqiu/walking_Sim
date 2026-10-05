using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerUnstuck : MonoBehaviour
{
    [Header("Triple Space")]
    public float pressTimeWindow = 1f;
    public int requiredPresses = 3;
    public float cooldown = 2f;

    [Header("Unstuck Jump")]
    public float jumpHeight = 10f;
    public float forwardDistance = 1f;

    public float gravity = 20f;


    public float maxFallSpeed = 30f;


    public float maxUnstuckTime = 5f;

    [Header("Ground Detection")]
    public LayerMask groundMask;
    public float groundCheckDistance = 0.5f;

    private PlayerMovement playerMovement;
    private CharacterController controller;

    private int pressCount = 0;
    private float firstPressTime;
    private float lastUnstuckTime = -999f;

    private bool isUnstucking = false;


    void Start()
    {
        playerMovement = GetComponent<PlayerMovement>();
        controller = GetComponent<CharacterController>();
    }


    void Update()
    {
        if (isUnstucking)
        {
            return;
        }

        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            RegisterSpacePress();
        }
    }


    void RegisterSpacePress()
    {
        // 2秒冷却
        if (Time.time < lastUnstuckTime + cooldown)
        {
            return;
        }



        if (pressCount == 0)
        {
            pressCount = 1;
            firstPressTime = Time.time;
            return;
        }


        if (Time.time - firstPressTime > pressTimeWindow)
        {
            pressCount = 1;
            firstPressTime = Time.time;
            return;
        }


        pressCount++;


  
        if (pressCount >= requiredPresses)
        {
            pressCount = 0;

            StartCoroutine(Unstuck());
        }
    }


    IEnumerator Unstuck()
{
    isUnstucking = true;
    lastUnstuckTime = Time.time;

    
    Vector3 upDirection = transform.up;
    Vector3 forwardDirection = transform.forward;

    
    if (playerMovement != null)
    {
        playerMovement.enabled = false;
    }


    if (controller != null)
    {
        controller.enabled = true;
    }

    
    float verticalSpeed =
        Mathf.Sqrt(2f * gravity * jumpHeight);

    
    float totalAirTime =
        (2f * verticalSpeed) / gravity;
    
    float forwardSpeed =
        forwardDistance / totalAirTime;


    float timer = 0f;


    while (timer < maxUnstuckTime)
    {
        timer += Time.deltaTime;

        

        verticalSpeed -= gravity * Time.deltaTime;


        if (verticalSpeed < -maxFallSpeed)
        {
            verticalSpeed = -maxFallSpeed;
        }




        Vector3 verticalMovement = upDirection * verticalSpeed * Time.deltaTime;



        Vector3 forwardMovement = forwardDirection * forwardSpeed * Time.deltaTime;



        Vector3 movement =
            verticalMovement +
            forwardMovement;


  

        if (controller != null && controller.enabled)
        {
            controller.Move(movement);
        }
        else
        {
            transform.position += movement;
        }




        if (verticalSpeed < 0f)
        {
            if (CheckGround(upDirection))
            {
                break;
            }
        }


        yield return null;
    }

    
    if (playerMovement != null)
    {
        playerMovement.enabled = true;
    }


    isUnstucking = false;

    Debug.Log("Player Unstuck Finished!");
}

    bool CheckGround(Vector3 upDirection)
    {
        Vector3 origin =
            transform.position +
            upDirection * 0.1f;


        return Physics.Raycast(
            origin,
            -upDirection,
            groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }
}