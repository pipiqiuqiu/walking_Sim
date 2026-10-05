using System.Collections;
using UnityEngine;

public class PawnMove : MonoBehaviour
{
    public float moveDistance = 0.2f;
    public float moveDuration = 2f;

    private bool hasMoved = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (hasMoved)
            {
                return;
            }

            hasMoved = true;

            StartCoroutine(MovePawn());
        }
        
    }

    IEnumerator MovePawn()
    {
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + Vector3.right * moveDistance;

        float timer = 0f;

        while (timer < moveDuration)
        {
            timer += Time.deltaTime;

            float t = timer / moveDuration;

            transform.position = Vector3.Lerp(startPosition, targetPosition,t);

            yield return null;
        }
        
        transform.position = targetPosition;
        
        PawnManager.instance.PawnFinishedMoving();
    }
}