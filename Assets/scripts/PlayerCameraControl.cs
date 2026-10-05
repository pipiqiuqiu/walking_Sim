using UnityEngine;

public class PlayerCameraControl : MonoBehaviour
{

    [SerializeField] private Transform cameraHolder;
    [SerializeField] private float mouseSensitivity;
    [SerializeField] private float stickSensitivity;

    private float rotationX = 0f;
    private float inputX;
    private float inputY;
    private Controls controls;

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
}

