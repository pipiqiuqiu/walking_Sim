using UnityEngine;

public class Queen_Float : MonoBehaviour
{
    public float floatDistance;
    public float floatSpeed;

    private Vector3 startPostion;

    void Start()
    {
        startPostion = transform.localPosition;
    }

    void Update()
    {
        Vector3 newPosition = startPostion;
        newPosition.y += Mathf.Sin(Time.time * floatSpeed) * floatDistance;
        transform.localPosition = newPosition;
    }

}
