using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;            // 玩家
    public float camDistance = 6f;      // 相机在身后多远
    public float camHeight = 3.0f;      // 相机高度
    public float pitch = 18f;           // 固定俯仰角(下看的度数) —— 锁定/解锁都用它,所以不会跳
    public float mouseSensitivity = 3f; // 解锁时鼠标水平转视角
    public float followDamping = 8f;    // 阻尼:越大越快追上;越小漂得越明显

    [Header("锁定 Lock-on")]
    public float lockRange = 8f;        // 锁定最大距离(单位)
    public float lockAngle = 45f;       // 锁定锥形半角(度)：只锁角色面前 ±此角度 内的敌人

    private float yaw = 0f;
    private Transform lockedTarget;
    private Vector3 followPos;

    public Transform CurrentTarget => lockedTarget;

    void Start()
    {
        if (target != null) followPos = target.position;
    }

    void Update()
    {
        // 鼠标中键 切换 锁定 / 解锁
        if (Input.GetMouseButtonDown(2))
        {
            if (lockedTarget != null)
            {
                lockedTarget = null;                 // 已锁定 → 解锁
            }
            else
            {
                Transform e = FindLockableEnemy();   // 找面前±lockAngle、lockRange内的敌人
                if (e != null)
                    lockedTarget = e;                // 锁定
                else
                    yaw = target.eulerAngles.y;      // 没有可锁定的敌人 → 视角回正到角色面朝方向
            }
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 阻尼跟随：跟随点慢慢追玩家 → 移动时玩家漂向移动方向，停下回正
        followPos = Vector3.Lerp(followPos, target.position, followDamping * Time.deltaTime);

        float useYaw;
        if (lockedTarget != null)
        {
            // 锁定：水平朝向敌人
            Vector3 dir = lockedTarget.position - target.position;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) dir = target.forward;
            useYaw = Quaternion.LookRotation(dir).eulerAngles.y;
            yaw = useYaw;   // 持续跟踪,解锁时无缝衔接
        }
        else
        {
            // 解锁：鼠标控制水平视角
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            useYaw = yaw;
        }

        // 位置：身后 camDistance + 高 camHeight；朝向：固定 pitch + useYaw
        // 关键：pitch 是常量 → 锁定/解锁俯仰角完全一致 → 不会跳变
        Vector3 flatDir = Quaternion.Euler(0, useYaw, 0) * Vector3.forward;
        transform.position = followPos - flatDir * camDistance + Vector3.up * camHeight;
        transform.rotation = Quaternion.Euler(pitch, useYaw, 0);
    }

    // 找"角色面前 ±lockAngle 锥形内、且 lockRange 距离内"最近的敌人
    Transform FindLockableEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        Vector3 fwd = target.forward; fwd.y = 0; fwd.Normalize();   // 角色当前面朝(水平)
        Transform best = null;
        float minDist = lockRange;
        foreach (GameObject e in enemies)
        {
            Vector3 to = e.transform.position - target.position;
            to.y = 0;
            float dist = to.magnitude;
            if (dist > lockRange) continue;                  // 太远
            if (Vector3.Angle(fwd, to) > lockAngle) continue; // 不在面前±lockAngle锥形内
            if (dist < minDist) { minDist = dist; best = e.transform; }
        }
        return best;
    }
}
