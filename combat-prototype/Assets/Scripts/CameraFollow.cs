using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;              // 跟随目标 (Cube)
    public float distance = 5f;           // 相机离 Cube 的距离
    public float mouseSensitivity = 3f;   // 鼠标灵敏度
    public float minPitch = -30f;         // 最小俯仰角(往下看的极限)
    public float maxPitch = 80f;          // 最大俯仰角(往上看的极限,负值=仰视)

    private float yaw = 0f;               // 水平旋转累加值
    private float pitch = 15f;            // 垂直旋转累加值(初始 15° 微俯视)

    void LateUpdate()
    {
        // 1. 读鼠标输入,累加到 yaw 和 pitch
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 2. 限制 pitch 范围,防止万向锁
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // 3. 根据 yaw 和 pitch 计算相机的旋转方向
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);

        // 4. 算出相机应该在的位置 (Cube 位置 - 相机朝向的反方向 × distance)
        Vector3 desiredPosition = target.position - rotation * Vector3.forward * distance;

        // 5. 把相机放到那个位置,并让相机看向 Cube
        transform.position = desiredPosition;
        transform.LookAt(target.position);
    }
}