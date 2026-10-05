using UnityEngine;
using UnityEngine.InputSystem;

public class BlueCubeConnector : MonoBehaviour
{
    [Header("Raycast")]
    public Camera playerCamera;
    public LayerMask clickableMask;
    public float maxDistance = 1000f;

    [Header("Connector")]
    public Material connectorMaterial;
    public float width = 1f;
    public float thickness = 0.2f;

    private Transform firstCube;


    void Update()
    {
        // 新版 Input System
        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            SelectCube();
        }
    }


    void SelectCube()
    {
        // 屏幕正中心
        Vector3 screenCenter = new Vector3(
            Screen.width / 2f,
            Screen.height / 2f,
            0f
        );


        // 从 Camera 向屏幕中心发射射线
        Ray ray =
            playerCamera.ScreenPointToRay(screenCenter);


        RaycastHit hit;


        if (Physics.Raycast(
            ray,
            out hit,
            maxDistance,
            clickableMask,
            QueryTriggerInteraction.Ignore))
        {
            // 必须是 BlueCube
            if (!hit.transform.CompareTag("BlueCube"))
            {
                Debug.Log(
                    "Hit object is not BlueCube: "
                    + hit.transform.name
                );

                return;
            }


            // =========================
            // 第一次点击
            // =========================

            if (firstCube == null)
            {
                firstCube = hit.transform;

                Debug.Log(
                    "First Cube: "
                    + firstCube.name
                );

                return;
            }


            // =========================
            // 第二次点击
            // =========================

            Transform secondCube =
                hit.transform;


            // 不允许连接自己
            if (secondCube == firstCube)
            {
                Debug.Log("Cannot connect the same cube.");

                return;
            }


            Debug.Log(
                "Second Cube: "
                + secondCube.name
            );


            // 创建连接
            CreateConnector(
                firstCube.position,
                secondCube.position
            );


            // 清空，等待下一组
            firstCube = null;
        }
        else
        {
            Debug.Log("Nothing hit.");
        }
    }


    void CreateConnector(
        Vector3 start,
        Vector3 end)
    {
        // A → B 的方向
        Vector3 direction =
            end - start;


        // 两点距离
        float distance =
            direction.magnitude;


        // 两点中心
        Vector3 middle =
            (start + end) / 2f;


        // =========================
        // 创建长方体
        // =========================

        GameObject connector =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        connector.name = "Connector";


        // =========================
        // Position
        // =========================

        connector.transform.position =
            middle;


        // =========================
        // Scale
        //
        // Z 是长度方向
        // =========================

        connector.transform.localScale =
            new Vector3(
                width, 
                thickness,
                distance
            );


        // =========================
        // Rotation
        //
        // Z轴朝向第二个Cube
        // =========================

        connector.transform.rotation =
            Quaternion.LookRotation(
                direction.normalized,
                Vector3.up
            );


        // =========================
        // Rotation Z 强制为 0
        // =========================

        Vector3 euler =
            connector.transform.eulerAngles;

        connector.transform.eulerAngles =
            new Vector3(
                euler.x,
                euler.y,
                0f
            );


        // =========================
        // Material
        // =========================

        if (connectorMaterial != null)
        {
            MeshRenderer renderer =
                connector.GetComponent<MeshRenderer>();

            renderer.material =
                connectorMaterial;
        }
    }


    // Scene窗口显示Camera射线
    void OnDrawGizmos()
    {
        if (playerCamera == null)
        {
            return;
        }

        Gizmos.DrawRay(
            playerCamera.transform.position,
            playerCamera.transform.forward * maxDistance
        );
    }
}