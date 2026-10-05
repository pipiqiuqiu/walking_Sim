using UnityEngine;

public class PlayerResetDistance : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    private SphereCollider sphereCollider;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private CharacterController controller;
    private Rigidbody rb;
    private PlayerMovement movement;


    void Start()
    {
        sphereCollider = GetComponent<SphereCollider>();

        if (sphereCollider == null)
        {
            Debug.LogError("PlayerResetDistance needs a SphereCollider!");
            enabled = false;
            return;
        }


        // 如果没有手动拖 Player，就通过 Tag 自动寻找
        if (player == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }


        if (player == null)
        {
            Debug.LogError("Cannot find Player!");
            enabled = false;
            return;
        }


        // 记录 Player 游戏开始时的位置和旋转
        startPosition = player.position;
        startRotation = player.rotation;

        controller = player.GetComponent<CharacterController>();
        rb = player.GetComponent<Rigidbody>();
        movement = player.GetComponent<PlayerMovement>();
    }


    void Update()
    {
        if (player == null || sphereCollider == null)
        {
            return;
        }


        // 检查 Player 是否还在 Sphere 内
        if (!IsPlayerInsideSphere())
        {
            ResetPlayer();
        }
    }


    bool IsPlayerInsideSphere()
    {
        // Sphere 中心转换成世界坐标
        Vector3 sphereCenter =
            sphereCollider.transform.TransformPoint(
                sphereCollider.center
            );


        // 考虑物体 Scale
        Vector3 scale =
            sphereCollider.transform.lossyScale;

        float largestScale =
            Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            );


        // Sphere 的真实世界半径
        float worldRadius =
            sphereCollider.radius * largestScale;


        // Player 到 Sphere 中心的距离
        float distance =
            Vector3.Distance(
                player.position,
                sphereCenter
            );


        return distance <= worldRadius;
    }


    void ResetPlayer()
    {
        if (movement != null && movement.isActiveAndEnabled)
        {
            movement.ResetToPose(startPosition, startRotation);
            Debug.Log("Player outside Sphere! Reset Player.");
            return;
        }

        bool controllerWasEnabled =
            controller != null && controller.enabled;


        // 暂时关闭 CharacterController
        if (controller != null)
        {
            controller.enabled = false;
        }


        // 只有非 Kinematic Rigidbody 才清除速度
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }


        // 回到出生位置
        player.position = startPosition;
        player.rotation = startRotation;


        // 如果 Rigidbody 存在，也同步它的位置
        if (rb != null)
        {
            rb.position = startPosition;
            rb.rotation = startRotation;
        }


        // 恢复 CharacterController
        if (controller != null)
        {
            controller.enabled = controllerWasEnabled;
        }


        Physics.SyncTransforms();

        Debug.Log("Player outside Sphere! Reset Player.");
    }
}
