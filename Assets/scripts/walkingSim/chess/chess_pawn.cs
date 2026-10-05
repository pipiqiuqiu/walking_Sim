using System.Collections;
using UnityEngine;

public class PawnMove : MonoBehaviour
{
    public float moveDistance = 0.2f;
    public float moveDuration = 2f;

    private bool isOut = false;
    private bool isMoving = false;
    private bool hasCounted = false;

    private Vector3 originalLocalPosition;

    void Start()
    {
        // 记录相对于父物体的位置
        originalLocalPosition = transform.localPosition;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (isMoving)
            {
                return;
            }

            StartCoroutine(MovePawn());
        }
    }

    IEnumerator MovePawn()
    {
        isMoving = true;

        // 使用 Local Position
        Vector3 startPosition = transform.localPosition;
        Vector3 targetPosition;

        if (!isOut)
        {
            // 沿自己的 Local X 方向推出
            targetPosition =
                originalLocalPosition + Vector3.right * moveDistance;
        }
        else
        {
            // 回到原来的 Local Position
            targetPosition = originalLocalPosition;
        }

        float timer = 0f;

        while (timer < moveDuration)
        {
            timer += Time.deltaTime;

            float t = timer / moveDuration;

            transform.localPosition = Vector3.Lerp(
                startPosition,
                targetPosition,
                t
            );

            yield return null;
        }

        transform.localPosition = targetPosition;

        isOut = !isOut;

        if (isOut && !hasCounted)
        {
            hasCounted = true;
            PawnManager.instance.PawnFinishedMoving();
        }

        isMoving = false;
    }
}