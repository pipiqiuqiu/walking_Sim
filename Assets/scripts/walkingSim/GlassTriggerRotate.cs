using System.Collections;
using UnityEngine;

public class GlassTriggerRotate : MonoBehaviour
{
    public Transform pivot;

    public float rotateDuration = 2f;
    public float rotateAngle = 180f;
    public float waitTime = 5f;

    private bool isRotating = false;
    private bool playerOnGlass = false;

    void OnTriggerEnter(Collider other)
    {
        // 只检测 Player
        if (!other.CompareTag("Player"))
            return;

        if (playerOnGlass || isRotating)
            return;

        playerOnGlass = true;

        StartCoroutine(WaitAndRotate());
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        playerOnGlass = false;
    }

    IEnumerator WaitAndRotate()
    {
        // Player 上来以后等待
        yield return new WaitForSeconds(waitTime);

        // 等待期间 Player 已经走了，就不旋转
        if (!playerOnGlass)
            yield break;

        yield return StartCoroutine(Rotate());
    }

    IEnumerator Rotate()
    {
        isRotating = true;

        float elapsed = 0f;
        float rotatedAngle = 0f;

        while (elapsed < rotateDuration)
        {
            float deltaTime = Time.deltaTime;

            float angleThisFrame =
                rotateAngle / rotateDuration * deltaTime;

            if (rotatedAngle + angleThisFrame > rotateAngle)
            {
                angleThisFrame = rotateAngle - rotatedAngle;
            }

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