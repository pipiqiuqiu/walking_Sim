using UnityEngine;

public class ChangeMaterial : MonoBehaviour
{
    public PawnManager pawnManager;

    public Material material1;
    public Material material2;

    private Renderer objectRenderer;

    void Start()
    {
        objectRenderer = GetComponent<Renderer>();
        objectRenderer.material = material1;
    }

    void Update()
    {
        if (pawnManager == null)
        {
            return;
        }
        if (pawnManager.pawnCount >= 8)
        {
            objectRenderer.material = material2;
        }
        else
        {
            objectRenderer.material = material1;
        }
    }
}