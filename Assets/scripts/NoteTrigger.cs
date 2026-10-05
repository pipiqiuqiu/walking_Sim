using System;
using UnityEngine;

public class NoteTrigger : MonoBehaviour
{
    
    public AudioSource note001;
    public GameObject door001;
    public void OnTriggerEnter(Collider other)
    {
        note001.Play();
        door001.SetActive(false);
    }

    public void OnTriggerExit(Collider other)
    {
        door001.SetActive(true);
    }

    void Start()
    {
        
    }

   
    void Update()
    {
        
    }
}
