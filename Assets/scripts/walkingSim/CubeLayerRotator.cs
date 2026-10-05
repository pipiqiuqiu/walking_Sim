using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CubeLayerRotator : MonoBehaviour
{
    public Transform cubeParent;
    public Transform rotationPivot;

    public float rotateSpeed = 180f;

    private HashSet<Transform> detected = new HashSet<Transform>();


    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("CubePiece"))
        {
            detected.Add(other.transform);
        }
    }


    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("CubePiece"))
        {
            detected.Remove(other.transform);
        }
    }


    public IEnumerator RotateLayer()
    {
        List<Transform> cubes = new List<Transform>();


        // 找到没有被Detect碰到的9个Cube
        foreach (Transform cube in cubeParent)
        {
            if (!detected.Contains(cube))
            {
                cubes.Add(cube);
            }
        }


        // 必须刚好是9个Cube
        if (cubes.Count != 9)
        {
            yield break;
        }


        // 旋转轴
        Vector3 axis = transform.right;


        // ==========================================
        // Cube挂到RotationPivot
        // ==========================================

        foreach (Transform cube in cubes)
        {
            cube.SetParent(
                rotationPivot,
                true
            );
        }


        // ==========================================
        // 旋转90度
        // ==========================================

        float rotated = 0f;

        while (rotated < 90f)
        {
            float step =
                rotateSpeed * Time.deltaTime;


            // 防止超过90度
            if (rotated + step > 90f)
            {
                step = 90f - rotated;
            }


            rotationPivot.Rotate(
                axis,
                step,
                Space.World
            );


            rotated += step;

            yield return null;
        }


        // ==========================================
        // Cube放回CubeParent
        // ==========================================

        foreach (Transform cube in cubes)
        {
            cube.SetParent(
                cubeParent,
                true
            );
        }


        // ==========================================
        // Reset RotationPivot
        // ==========================================

        rotationPivot.rotation =
            Quaternion.identity;
    }
}