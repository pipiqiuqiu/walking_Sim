using UnityEngine;

public class PeopleMove : MonoBehaviour
{

    //int lives = 3;
    //public string name = "Bob";
    //bool gameOver = false;

    public float speed = 3f;
    public Transform startPoint;
    public Transform endPoint;
    public Transform endTurnPoint;
    public Transform startTurnPoint;
    
    private Transform[] points;
    private int currentPoint = 0;
    
    void Start()
    {
        points = new Transform[]
        { 
            startPoint, endPoint, endTurnPoint, startTurnPoint
        };
        
        currentPoint = 0;
    }


    void Update()
    {
        Transform target = points[currentPoint];
        
        transform.position = Vector3.MoveTowards(
            transform.position, 
            target.position, 
            speed * Time.deltaTime
            );
        
        if (Vector3.Distance(transform.position, target.position) < 0.1f)
        {
            currentPoint++;
            if (currentPoint >= points.Length)
            {
                currentPoint = 0;
            }
        }
        
    }
}
