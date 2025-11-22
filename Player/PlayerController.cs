using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    private enum PlayerState { Grounded, Jumping, Falling, Dead }

    [Header("Gravity Settings")]
    public float upwardGravity = 1.5f;
    public float downwardGravity = 4f;
    public float jumpCutGravity = 6f;

    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float jumpForce = 75f;
    private int maxJumpCount = 2;
    private float jumpCooldown = 0.2f;
    private float lastJumpTime = 0f;

    [Header("References")]
    private Rigidbody2D rb;
    private HingeJoint2D joint;
    private Rigidbody2D headRb;

    [Header("Input Actions")]
    private InputAction moveAction;
    private InputAction jumpAction;

    private Vector2 moveInput;
    private int jumpCount;
    private bool jumpRequested;
    private bool isHoldingJump;

    private PlayerState state = PlayerState.Grounded;
    private Coroutine rotationCoroutine;

    // ---------------------------------------------------------------
    // Initialization
    // ---------------------------------------------------------------
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        joint = GetComponent<HingeJoint2D>();
        headRb = GameObject.FindGameObjectWithTag("PlayerHead").GetComponent<Rigidbody2D>();

        joint.enabled = false;

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");

        jumpCount = maxJumpCount;
    }

    // ---------------------------------------------------------------
    // Update: Input + State transitions
    // ---------------------------------------------------------------
    void Update()
    {
        if (state == PlayerState.Dead)
            return;

        moveInput = moveAction.ReadValue<Vector2>();

        // Jump held?
        isHoldingJump = jumpAction.ReadValue<float>() > 0.5f;

        // Jump pressed?
        if (jumpAction.triggered &&
            Time.time - lastJumpTime > jumpCooldown &&
            jumpCount > 0)
        {
            RequestJump();
        }
    }

    private void RequestJump()
    {
        jumpRequested = true;
        lastJumpTime = Time.time;
        jumpCount--;
        state = PlayerState.Jumping;

        // DOUBLE JUMP ANIMATION ROTATION
        if (jumpCount == 0)
        {
            Balance balance = GetComponent<Balance>();
            if (balance != null)
            {
                balance.smoothSpeed = 1000000f;
                float direction = (moveInput.x <= 0) ? 1f : -1f;

                if (rotationCoroutine != null)
                    StopCoroutine(rotationCoroutine);

                rotationCoroutine = StartCoroutine(
                    LerpTargetRotation(balance, 0f, 360f * direction, 0.3f)
                );
            }
        }
    }

    // ---------------------------------------------------------------
    // FixedUpdate: Physics
    // ---------------------------------------------------------------
    void FixedUpdate()
    {
        HandleMovement();
        HandleGravity();
        HandleHeadJoint();

        if (jumpRequested)
            ExecuteJump();
    }

    private void HandleMovement()
    {
        Vector2 vel = rb.linearVelocity;
        vel.x = moveInput.x * moveSpeed;
        rb.linearVelocity = vel;

        if (rb.linearVelocity.y < -0.1f && state != PlayerState.Falling)
            state = PlayerState.Falling;
    }

    private void HandleGravity()
    {
        // FORCE full downward gravity after double-jump
        if (jumpCount == 0)
        {
            rb.gravityScale = downwardGravity;
            return;
        }

        float vy = rb.linearVelocity.y;

        if (vy > 0.01f) // going up
        {
            rb.gravityScale = isHoldingJump ? upwardGravity : jumpCutGravity;
        }
        else if (vy < -0.01f) // going down
        {
            rb.gravityScale = downwardGravity;
        }
        else
        {
            rb.gravityScale = 1f; // grounded or idle
        }
    }

    private void HandleHeadJoint()
    {
        Vector2 predicted = headRb.position + headRb.linearVelocity * Time.fixedDeltaTime;

        if (Vector2.Distance(predicted, rb.position) > 0.5f &&
            state == PlayerState.Falling)
        {
            joint.enabled = true;
        }
    }

    private void ExecuteJump()
    {
        jumpRequested = false;

        // reset Y velocity for consistency
        rb.linearVelocityY = 0f;

        float force = (jumpCount != 0) ? jumpForce : jumpForce * 1.25f;
        rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);
    }

    // ---------------------------------------------------------------
    // Rotation coroutine (double-jump animation)
    // ---------------------------------------------------------------
    IEnumerator LerpTargetRotation(Balance balance, float from, float to, float duration)
    {
        joint.enabled = true;
        float t = 0f;

        while (t < duration)
        {
            balance.targetRotation = Mathf.Lerp(from, to, t / duration);
            t += Time.deltaTime;
            yield return null;
        }

        // hold the rotation briefly
        float extra = 0.3f;
        while (t < duration + extra)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // reset
        joint.enabled = false;
        balance.smoothSpeed = 10f;
        balance.targetRotation = 0f;
    }

    // ---------------------------------------------------------------
    // Death Handling
    // ---------------------------------------------------------------
    IEnumerator HandleDeath()
    {
        state = PlayerState.Dead;
        joint.enabled = false;

        headRb.GetComponent<SpringJoint2D>().enabled = false;
        yield return new WaitForSeconds(0.5f);

        GameManager.isGameOver = true;
    }

    // ---------------------------------------------------------------
    // Collision Events
    // ---------------------------------------------------------------
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (state == PlayerState.Dead)
            return;

        // Instant kill
        if (collision.gameObject.layer == LayerMask.NameToLayer("InstantFatal"))
        {
            Instantiate(Resources.Load<GameObject>("Prefabs/Effects/Blood"), transform.position, Quaternion.identity);
            StartCoroutine(HandleDeath());
            return;
        }

        // Reset when grounded
        joint.enabled = false;
        jumpCount = maxJumpCount;

        if (state == PlayerState.Falling || state == PlayerState.Jumping)
            state = PlayerState.Grounded;
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (state != PlayerState.Dead)
            state = PlayerState.Falling;
    }
}
