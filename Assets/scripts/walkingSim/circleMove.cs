using UnityEngine;

public class CircleMove : MonoBehaviour
{
    public Transform center;
    public float radius = 5f;
    public float speed = 1f;

    private float angle = 0f;

    void Update()
    {
        angle += speed * Time.deltaTime;

        float x = center.position.x + Mathf.Cos(angle) * radius;
        float z = center.position.z + Mathf.Sin(angle) * radius;

        transform.position = new Vector3(
            x,
            transform.position.y,
            z
        );
    }
}