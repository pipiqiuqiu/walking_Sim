using System.Collections;
using UnityEngine;

public class DetectRotate : MonoBehaviour
{
    public float rotateTime = 2f;

    public CubeLayerRotator cubeLayerRotator;

    void Start()
    {
        StartCoroutine(RotateLoop());
    }

    IEnumerator RotateLoop()
    {
        while (true)
        {
            // 等待下一次旋转
            yield return new WaitForSeconds(rotateTime);

            // Detect随机转90度
            RotateRandom();

            // 非常重要：
            // 等物理系统更新Trigger
            yield return new WaitForFixedUpdate();

            // 让对应的9个Cube旋转
            yield return cubeLayerRotator.RotateLayer();
        }
    }

    void RotateRandom()
    {
        Vector3[] directions =
        {
            Vector3.right,
            Vector3.up,
            Vector3.forward
        };

        Vector3 axis = directions[Random.Range(0, 3)];

        transform.Rotate(
            axis,
            90f,
            Space.World
        );
    }
}