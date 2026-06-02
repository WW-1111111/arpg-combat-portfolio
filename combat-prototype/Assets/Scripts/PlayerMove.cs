using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    private Rigidbody rb;

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
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance, groundLayer);
    }
}