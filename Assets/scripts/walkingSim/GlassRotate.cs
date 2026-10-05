using System.Collections;
using UnityEngine;

public class RotateAroundPivot : MonoBehaviour
{
    [Header("Rotation")]
    public Transform pivot;

    public float waitTime = 5f;
    public float rotateDuration = 2f;
    public float rotateAngle = 180f;

    private bool isRotating = false;

    void Start()
    {
        StartCoroutine(RotateLoop());
    }

    IEnumerator RotateLoop()
    {
        while (true)
        {
            // 每次旋转前等待 5 秒
            yield return new WaitForSeconds(waitTime);

            // 开始旋转
            yield return StartCoroutine(Rotate());
        }
    }

    IEnumerator Rotate()
    {
        isRotating = true;

        float elapsed = 0f;
        float rotatedAngle = 0f;

        while (elapsed < rotateDuration)
        {
            float deltaTime = Time.deltaTime;

            // v of rotation
            float angleThisFrame =
                rotateAngle / rotateDuration * deltaTime;

            // do not exceed 180
            if (rotatedAngle + angleThisFrame > rotateAngle)
            {
                angleThisFrame = rotateAngle - rotatedAngle;
            }

            // around pivot rotate
            transform.RotateAround(
                pivot.position,
                pivot.forward,
                angleThisFrame
            );

            rotatedAngle += angleThisFrame;
            elapsed += deltaTime;

            yield return null;
        }

        isRotating = false;
    }
}