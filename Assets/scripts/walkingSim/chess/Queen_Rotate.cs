using UnityEngine;
using UnityEngine.UIElements;

public class Queen_Rotate : MonoBehaviour
{

    public float rotateSpeed;
    public PawnManager PawnManager;
    public Transform Queen;
    public float moveSpeed = 5f;
    private Vector3  Q;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Q = Queen.position;
    }

    // Update is called once per frame
    void Update()
    {
            rotateQueen();
    }

    void rotateQueen()
    {
        if (PawnManager.IsUnlocked)
        {
            transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.Self);
        }

        float targetY = 2*PawnManager.pawnCount+8;
        Q.y = Mathf.MoveTowards(Q.y, targetY, moveSpeed * Time.deltaTime);    
        Queen.position = Q;

    }
}
