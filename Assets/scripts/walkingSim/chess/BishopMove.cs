using System.Collections;
using UnityEngine;

public class BishopMove : MonoBehaviour
{

    [Header("Path")]
    public Transform[] points;
    
    [Header("Movement")]
    public float startSpeed = 5f;
    public float maxSpeed = 5f;
    public float accelerationTime = 2f;
    
    public float stayTime = 5f;
    public float startDelay = 0f;
    

    
    void Start()
    {
        
        if (points.Length == 0)
        {
            return;
        }
        
        transform.position = points[0].position;
        StartCoroutine(MoveLoop());
    }

    IEnumerator MoveLoop()
    {
        yield return new WaitForSeconds(startDelay);

        while (true)
        {
            for (int i = 1; i < points.Length; i++)
            {
                yield return StartCoroutine(MoveTo(points[i].position));

                yield return new WaitForSeconds(stayTime);
            }

            for (int i = points.Length - 2; i >= 0; i--)
            {
                yield return StartCoroutine(MoveTo(points[i].position));

                yield return new WaitForSeconds(stayTime);
            }
            
        }
    }

    IEnumerator MoveTo(Vector3 targetPosition)
    {
        float timer = 0f;
        Vector3 direction = (targetPosition - transform.position).normalized;

        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(-direction);
        }
        
        while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            timer += Time.deltaTime;
            
            float t = Mathf.Clamp01(timer / accelerationTime);

            float currentSpeed = Mathf.Lerp(startSpeed, maxSpeed, t);
            
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                currentSpeed * Time.deltaTime
            );

            yield return null;
        }

        transform.position = targetPosition;
    }
}