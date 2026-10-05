using UnityEngine;

public class NotesInSpace : MonoBehaviour
{

    public AudioSource Sound001;
    public float delayMin = 1.0f;
    public float delayMax = 3.0f;
    private float currentDelay = 1.0f;
    
    void Start()
    {
        //Sound001.Play();
        currentDelay = GetDelayTime();
    }

    float GetDelayTime()
    {
        return Random.Range( delayMin, delayMax);
    }

    void Update()
    {
        if (currentDelay > 0.0f)
        {
            currentDelay -= Time.deltaTime;
        }

        if (currentDelay <= 0.0f)
        {
            //Sound001.Play();
            Sound001.PlayOneShot(Sound001.clip);
            currentDelay = GetDelayTime();
        }
        
    }
}
