using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    private Rigidbody rb;
    private CapsuleCollider capsule;

    public float speed = 5f;
    public float jumpForce = 10f;
    public float airControl = 5f;
    public float fallMultiplier = 15f;
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.6f;

    private Vector3 inputDirection;
    private bool jumpPressed;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
    }

    void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // 拿到相机的水平前方和水平右方
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        // 把输入转成"相对相机"的方向
        inputDirection = camForward * v + camRight * h;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpPressed = true;
        }
    }

    void FixedUpdate()
    {
        //Debug.Log("Grounded=" + IsGrounded() + ", PosY=" + transform.position.y + ", v=" + Input.GetAxis("Vertical"));
        if (IsGrounded())
        {
            Vector3 movement = inputDirection * speed;
            rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);
        }
        else
        {
            Vector3 airForce = inputDirection * airControl;
            rb.AddForce(airForce, ForceMode.Acceleration);
        }

        if (IsGrounded() && jumpPressed)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
        }

        if (rb.linearVelocity.y < 0)
        {
            rb.AddForce(Vector3.down * fallMultiplier, ForceMode.Acceleration);
        }

        jumpPressed = false;
    }

    bool IsGrounded()
    {
        // 计算 Capsule 底部的世界坐标
        Vector3 capsuleBottom = transform.position + capsule.center - new Vector3(0, capsule.height / 2, 0);
        // 起点向上抬一点点（0.05），避免起点正好在 Plane 表面（边界 case 物理不稳定）
        Vector3 rayOrigin = capsuleBottom + Vector3.up * 0.05f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer);
    }
}