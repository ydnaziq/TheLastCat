using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    private enum PlayerState { Grounded, Jumping, Falling, Dead }

    [Header("Gravity Settings")]
    [Tooltip("Normal gravity while rising when holding jump")]
    public float upwardGravity = 1.5f;
    [Tooltip("Gravity while falling")]
    public float downwardGravity = 4f;
    [Tooltip("Gravity when jump is cut (short press)")]
    public float jumpCutGravity = 6f;
    [Tooltip("Maximum downward speed")]
    public float terminalVelocity = 30f;

    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    [Tooltip("Multiplier applied to moveSpeed while in air")]
    [Range(0f, 1f)]
    public float airControlMultiplier = 0.8f;

    [Tooltip("Base jump impulse force")]
    public float jumpForce = 14f;
    [Tooltip("Maximum number of jumps (1 = single, 2 = double)")]
    public int maxJumpCount = 2;

    [Header("Jump Feel")]
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;
    public float jumpCooldown = 0.08f;

    [Header("Wave Dash Settings")]
    public float waveDashHorizontalForce = 16f;
    public float waveDashVerticalForce = -12f;
    public float waveDashDuration = 0.18f;
    public float waveDashInputWindow = 0.22f;

    [Header("Dash Settings")]
    public float dashForce = 18f;
    public float dashDuration = 0.15f;

    private bool isDashing = false;
    private float dashTime = 0f;
    private Vector2 dashStartVel;
    private Vector2 dashTargetVel;

    private InputAction dashAction;

    [Header("Wave Dash Settings")]
    private float waveDashTimer = 0f;
    private bool waveDashQueued = false;
    private bool isWaveDashing = false;
    private float waveDashTime = 0f;
    private Vector2 waveDashStartVel;
    private Vector2 waveDashTargetVel;

    [Header("Ground Detection")]
    public LayerMask groundLayers;
    [Range(0f, 90f)]
    public float maxGroundAngle = 60f;
    public float groundCheckRadius = 0.1f;
    [Tooltip("Offset from object pivot for ground-check")]
    public Vector2 groundCheckOffset = new Vector2(0f, -0.5f);

    [Header("References (assign in inspector when possible)")]
    public Rigidbody2D headRb;
    private Rigidbody2D rb;
    private Balance balance;

    [Header("Input (auto-resolve using InputSystem.actions)")]
    private InputAction moveAction;
    private InputAction jumpAction;

    // Internal state
    private Vector2 moveInput;
    private int availableJumps;
    private bool jumpRequestedThisFrame;
    private float lastJumpTime = -999f;

    private float coyoteTimer = 0f;
    private float jumpBufferTimer = 0f;

    private PlayerState state = PlayerState.Grounded;
    private Coroutine rotationCoroutine;

    private bool isDead = false;

    // -------------------------------------------------------------------------
    // Unity event methods
    // -------------------------------------------------------------------------
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        balance = GetComponent<Balance>();

        if (headRb == null)
        {
            var headObj = GameObject.FindGameObjectWithTag("PlayerHead");
            if (headObj != null)
                headRb = headObj.GetComponent<Rigidbody2D>();
        }

        // initialize jump counts
        availableJumps = maxJumpCount;
    }

    void OnEnable()
    {
        // Resolve and enable actions (safe if InputSystem.actions exists)
        moveAction = InputSystem.actions?.FindAction("Move");
        jumpAction = InputSystem.actions?.FindAction("Jump");
        dashAction = InputSystem.actions?.FindAction("Dash");

        dashAction?.Enable();
        moveAction?.Enable();
        jumpAction?.Enable();
    }

    void OnDisable()
    {
        dashAction?.Disable();
        moveAction?.Disable();
        jumpAction?.Disable();
    }

    void Update()
    {
        if (isDead) return;

        // Read move input (kept in Update so input is fluid)
        moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        // Jump buffering: if player pressed jump this frame, start buffer timer
        if (jumpAction != null)
        {
            // Unity InputSystem: triggered is true on press
            if (jumpAction.triggered)
            {
                jumpBufferTimer = jumpBufferTime;
            }
        }

        if (!isDead && dashAction != null && dashAction.triggered)
        {
            float direction = moveInput.x != 0 ? Mathf.Sign(moveInput.x) : 1f;
            StartDash(direction);
        }

        // Decrease timers
        if (coyoteTimer > 0f) coyoteTimer -= Time.deltaTime;
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        // If buffered jump exists and we are allowed to jump, request jump
        if (jumpBufferTimer > 0f && (coyoteTimer > 0f || availableJumps > 0) && Time.time - lastJumpTime > jumpCooldown)
        {
            RequestJump();
            jumpBufferTimer = 0f; // consume buffer
        }

        // Track state from vertical velocity (for visuals / other logic)
        if (!isDead)
        {
            if (rb.linearVelocity.y < -0.1f && state != PlayerState.Falling)
                state = PlayerState.Falling;
        }

        bool jumpPressed = jumpAction != null && jumpAction.triggered;
        bool holdingDown = moveInput.y < -0.5f;

        if (jumpPressed && holdingDown)
        {
            waveDashQueued = true;
            waveDashTimer = waveDashInputWindow;
        }
        else
        {
            if (waveDashTimer > 0f)
                waveDashTimer -= Time.deltaTime;
            else
                waveDashQueued = false;
        }
    }

    void FixedUpdate()
    {
        if (isDead) return;

        UpdateGroundedState();

        HandleMovementPhysics();
        HandleDashPhysics();

        HandleGravityPhysics();
        HandleWaveDashPhysics();

        // Execute jump after physics preparation so we can zero vertical velocity cleanly
        if (jumpRequestedThisFrame)
        {
            ExecuteJump();
            jumpRequestedThisFrame = false;
        }
    }

    // -------------------------------------------------------------------------
    // Movement & Gravity
    // -------------------------------------------------------------------------
    private void HandleMovementPhysics()
    {
        float control = (state == PlayerState.Grounded) ? 1f : airControlMultiplier;
        Vector2 vel = rb.linearVelocity;
        vel.x = moveInput.x * moveSpeed * control;
        rb.linearVelocity = vel;
    }

    private void HandleGravityPhysics()
    {
        float vy = rb.linearVelocity.y;

        if (vy > 0.01f) // going up
        {
            // if player holds jump (InputSystem: check value)
            bool holdingJump = jumpAction != null && jumpAction.ReadValue<float>() > 0.5f;
            rb.gravityScale = holdingJump ? upwardGravity : jumpCutGravity;
        }
        else if (vy < -0.01f) // going down
        {
            rb.gravityScale = downwardGravity;
        }
        else
        {
            // near zero vertical velocity - keep default gravity scale
            rb.gravityScale = 1f;
        }

        // clamp terminal velocity
        if (rb.linearVelocity.y < -terminalVelocity)
        {
            Vector2 v = rb.linearVelocity;
            v.y = -terminalVelocity;
            rb.linearVelocity = v;
        }
    }

    // -------------------------------------------------------------------------
    // Jumping
    // -------------------------------------------------------------------------
    private void RequestJump()
    {
        jumpRequestedThisFrame = true;
    }

    private void ExecuteJump()
    {
        // sanity guard
        if (Time.time - lastJumpTime < jumpCooldown) return;

        // If grounded or within coyote time we can still jump even if availableJumps is max
        bool allowed = (coyoteTimer > 0f) || (availableJumps > 0);
        if (!allowed) return;

        lastJumpTime = Time.time;

        // decrement jumps only when not grounded (so that from ground you keep one less)
        if (state != PlayerState.Grounded)
            availableJumps = Mathf.Max(0, availableJumps - 1);
        else
            availableJumps = Mathf.Max(0, availableJumps - 1); // on-ground consumes 1 jump too

        // reset vertical velocity for consistent jump feel
        Vector2 v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        // apply jump impulse
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        // update state
        state = PlayerState.Jumping;

        // if this was a mid-air/double jump, play rotation animation on Balance
        if (availableJumps == 0 && balance != null)
        {
            // kill previous coroutine if any
            if (rotationCoroutine != null) StopCoroutine(rotationCoroutine);
            rotationCoroutine = StartCoroutine(DoDoubleJumpRotation(0.3f));
        }
    }

    // -------------------------------------------------------------------------
    // Ground detection (contact normal based)
    // -------------------------------------------------------------------------
    private void UpdateGroundedState()
    {
        bool grounded = false;

        // Prefer physics-ground-check by casting a small circle near feet
        Vector2 checkCenter = (Vector2)transform.position + groundCheckOffset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(checkCenter, groundCheckRadius, groundLayers);

        if (hits != null && hits.Length > 0)
        {
            // Check contact normals via colliders -> use the collider's bounds to approximate normal by sample raycast
            foreach (var c in hits)
            {
                // cast a short ray from slightly above to point toward collider to get normal from the contact point
                RaycastHit2D hit = Physics2D.Raycast((Vector2)transform.position + Vector2.up * 0.1f, (c.transform.position - transform.position).normalized, 1f, groundLayers);
                if (hit.collider != null)
                {
                    float angle = Vector2.Angle(hit.normal, Vector2.up);
                    if (angle <= maxGroundAngle)
                    {
                        grounded = true;
                        break;
                    }
                }
                else
                {
                    // fallback: if collider is below player, consider ground (works for simple setups)
                    if (c.bounds.center.y <= transform.position.y)
                    {
                        grounded = true;
                        break;
                    }
                }
            }
        }

        if (grounded)
        {
            // reset jump counters when touching ground
            availableJumps = maxJumpCount;
            coyoteTimer = coyoteTime;
            if (state == PlayerState.Falling || state == PlayerState.Jumping)
                state = PlayerState.Grounded;
        }
        else
        {
            // if we just left the ground, start coyote timer (only when previously grounded)
            if (state == PlayerState.Grounded)
                coyoteTimer = coyoteTime;

            if (state != PlayerState.Dead)
                state = PlayerState.Falling;
        }
    }

    private IEnumerator DoDoubleJumpRotation(float duration)
    {
        if (balance == null) yield break;

        float previousSmooth = balance.smoothSpeed;
        balance.smoothSpeed = 10000f;
        float elapsed = 0f;

        float from = balance.targetRotation;
        float to = from + 360f * (moveInput.x <= 0 ? 1f : -1f);

        var joint = GetComponent<HingeJoint2D>();
        if (joint != null) joint.enabled = true;

        while (elapsed < duration)
        {
            balance.targetRotation = Mathf.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // allow a small hold
        yield return new WaitForSeconds(0.15f);

        // reset
        if (joint != null) joint.enabled = false;
        balance.smoothSpeed = previousSmooth;
        balance.targetRotation = 0f;
    }

    private void HandleWaveDashPhysics()
    {
        if (isWaveDashing)
        {
            waveDashTime += Time.fixedDeltaTime;
            float t = waveDashTime / waveDashDuration;
            Vector2 lerped = Vector2.Lerp(waveDashStartVel, waveDashTargetVel, t);
            rb.linearVelocity = lerped;

            if (t >= 1f)
            {
                isWaveDashing = false;
                rb.gravityScale = 1f;
            }
            return;
        }

        bool recentlyJumped = (Time.time - lastJumpTime) <= waveDashInputWindow;

        if (waveDashQueued && recentlyJumped)
        {
            waveDashQueued = false;

            float dir = moveInput.x != 0 ? Mathf.Sign(moveInput.x) : 1f;

            waveDashStartVel = rb.linearVelocity;
            waveDashTargetVel = new Vector2(dir * waveDashHorizontalForce, waveDashVerticalForce);
            waveDashTime = 0f;

            isWaveDashing = true;
            rb.gravityScale = downwardGravity * 1.5f;

            if (availableJumps > 0)
                availableJumps--;

            state = PlayerState.Falling;
        }
    }

    private void StartDash(float direction)
    {
        dashStartVel = rb.linearVelocity;
        dashTargetVel = new Vector2(direction * dashForce, 0f);
        dashTime = 0f;
        isDashing = true;
        rb.gravityScale = 0f;
        IgnoreDashCollisions();

        state = PlayerState.Falling;
    }

    private void HandleDashPhysics()
    {
        if (!isDashing) return;

        dashTime += Time.fixedDeltaTime;
        float t = dashTime / dashDuration;
        rb.linearVelocity = Vector2.Lerp(dashStartVel, dashTargetVel, t);

        if (headRb != null)
            headRb.position += (rb.linearVelocity - dashStartVel) * Time.fixedDeltaTime;

        if (t >= 1f)
        {
            isDashing = false;
            rb.gravityScale = 1f;
            RestoreDashCollisions();
        }
    }

    private void IgnoreDashCollisions()
    {
        if (headRb != null)
        {
            foreach (int layer in new int[] { LayerMask.NameToLayer("Fatal"), LayerMask.NameToLayer("Enemy") })
                Physics2D.IgnoreLayerCollision(headRb.gameObject.layer, layer, true);
        }
        foreach (int layer in new int[] { LayerMask.NameToLayer("Fatal"), LayerMask.NameToLayer("Enemy") })
            Physics2D.IgnoreLayerCollision(gameObject.layer, layer, true);
    }

    private void RestoreDashCollisions()
    {
        if (headRb != null)
        {
            foreach (int layer in new int[] { LayerMask.NameToLayer("Fatal"), LayerMask.NameToLayer("Enemy") })
                Physics2D.IgnoreLayerCollision(headRb.gameObject.layer, layer, false);
        }
        foreach (int layer in new int[] { LayerMask.NameToLayer("Fatal"), LayerMask.NameToLayer("Enemy") })
            Physics2D.IgnoreLayerCollision(gameObject.layer, layer, false);
    }

    public IEnumerator HandleDeath()
    {
        if (isDead) yield break; // prevent re-entry
        isDead = true;
        state = PlayerState.Dead;

        // disable movement and zero velocity
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Dynamic;
        var pj = GetComponent<Collider2D>();
        if (pj != null) pj.enabled = false;

        // disable head spring if present
        if (headRb != null)
        {
            var spring = headRb.GetComponent<SpringJoint2D>();
            if (spring != null) spring.enabled = false;
        }

        yield return new WaitForSeconds(0.5f);
        GameManager.isGameOver = true;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        // Instant kill layer check
        int instaLayer = LayerMask.NameToLayer("InstantFatal");
        if (collision.gameObject.layer == instaLayer)
        {
            var bloodPrefab = Resources.Load<GameObject>("Prefabs/Effects/Blood");
            if (bloodPrefab != null)
                Instantiate(bloodPrefab, transform.position, Quaternion.identity);

            StartCoroutine(HandleDeath());
            return;
        }

        foreach (var contact in collision.contacts)
        {
            float angle = Vector2.Angle(contact.normal, Vector2.up);
            if (angle <= maxGroundAngle)
            {
                // ground contact found
                availableJumps = maxJumpCount;
                coyoteTimer = coyoteTime;
                state = PlayerState.Grounded;
                break;
            }
        }
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        // Do not mark falling on exit if still has some grounded contacts (complex scenes).
        // A robust approach would maintain a contact counter per ground collider. For simplicity:
        if (isDead) return;

        // we leave collision -> start falling, but only if vertical velocity is negative
        if (rb.linearVelocity.y < 0f)
            state = PlayerState.Falling;
    }

    // -------------------------------------------------------------------------
    // Debug gizmos
    // -------------------------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector2 checkCenter = (Vector2)transform.position + groundCheckOffset;
        Gizmos.DrawWireSphere(checkCenter, groundCheckRadius);

        // draw coyote / buffer debug text (only in editor)
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, $"State: {state}");
#endif
    }
}
