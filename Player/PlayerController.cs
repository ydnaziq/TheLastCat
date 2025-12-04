using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    #region Enums
    private enum PlayerState { Grounded, Jumping, Falling, Dead }
    #endregion

    #region Inspector Settings

    [Header("Gravity Settings")]
    [Tooltip("Normal gravity while rising when holding jump")] public float upwardGravity = 1.5f;
    [Tooltip("Gravity while falling")] public float downwardGravity = 4f;
    [Tooltip("Gravity when jump is cut (short press)")] public float jumpCutGravity = 6f;
    [Tooltip("Maximum downward speed")] public float terminalVelocity = 30f;

    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    [Range(0f, 1f), Tooltip("Multiplier applied to moveSpeed while in air")] public float airControlMultiplier = 0.8f;
    [Tooltip("Base jump impulse force")] public float jumpForce = 14f;
    [Tooltip("Maximum number of jumps (1 = single, 2 = double)")] public int maxJumpCount = 2;

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

    [Header("Ground Detection")]
    public LayerMask groundLayers;
    [Range(0f, 90f)] public float maxGroundAngle = 60f;
    public float groundCheckRadius = 0.1f;
    [Tooltip("Offset from object pivot for ground-check")] public Vector2 groundCheckOffset = new Vector2(0f, -0.5f);

    [Header("References")]
    [SerializeField] private ParticleSystem trailParticles;
    public Rigidbody2D headRb; // optional, auto-find if null

    #endregion

    #region Private Fields

    private Rigidbody2D rb;
    private Balance balance;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;

    private Vector2 moveInput;
    private int availableJumps;
    private bool jumpRequestedThisFrame;
    private float lastJumpTime = -999f;

    private float coyoteTimer = 0f;
    private float jumpBufferTimer = 0f;

    private PlayerState state = PlayerState.Grounded;
    private Coroutine rotationCoroutine;
    private bool isDead = false;

    // Dash state
    private bool isDashing = false;
    private float dashTime = 0f;
    private Vector2 dashStartVel;
    private Vector2 dashTargetVel;

    // Wave dash state
    private bool waveDashQueued = false;
    private bool isWaveDashing = false;
    private float waveDashTimer = 0f;
    private float waveDashTime = 0f;
    private Vector2 waveDashStartVel;
    private Vector2 waveDashTargetVel;

    #endregion

    #region Unity Callbacks

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        balance = GetComponent<Balance>();

        // Auto-find head Rigidbody if not assigned
        if (headRb == null)
        {
            var headObj = GameObject.FindGameObjectWithTag("PlayerHead");
            if (headObj != null)
                headRb = headObj.GetComponent<Rigidbody2D>();
        }

        availableJumps = maxJumpCount;

    }

    void OnEnable()
    {
        moveAction = InputSystem.actions?.FindAction("Move");
        jumpAction = InputSystem.actions?.FindAction("Jump");
        dashAction = InputSystem.actions?.FindAction("Dash");

        moveAction?.Enable();
        jumpAction?.Enable();
        dashAction?.Enable();
    }

    void OnDisable()
    {
        moveAction?.Disable();
        jumpAction?.Disable();
        dashAction?.Disable();
    }

    void Update()
    {
        if (isDead) return;

        HandleInput();
    }

    void FixedUpdate()
    {
        if (isDead) return;

        UpdateGroundedState();

        HandleMovementPhysics();
        HandleGravityPhysics();
        HandleDashPhysics();
        HandleWaveDashPhysics();

        if (jumpRequestedThisFrame)
        {
            ExecuteJump();
            jumpRequestedThisFrame = false;
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead) return;

        // Instant kill
        if (collision.gameObject.layer == LayerMask.NameToLayer("InstantFatal"))
        {
            var bloodPrefab = Resources.Load<GameObject>("Prefabs/Effects/Blood");
            if (bloodPrefab != null)
                Instantiate(bloodPrefab, transform.position, Quaternion.identity);

            StartCoroutine(HandleDeath());
            return;
        }

        foreach (var contact in collision.contacts)
        {
            if (Vector2.Angle(contact.normal, Vector2.up) <= maxGroundAngle)
            {
                availableJumps = maxJumpCount;
                coyoteTimer = coyoteTime;
                state = PlayerState.Grounded;
                break;
            }
        }
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (isDead) return;
        if (rb.linearVelocity.y < 0f) state = PlayerState.Falling;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere((Vector2)transform.position + groundCheckOffset, groundCheckRadius);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, $"State: {state}");
#endif
    }

    #endregion

    #region Input Handling

    private void HandleInput()
    {
        // Read movement input
        moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        bool jumpPressed = jumpAction != null && jumpAction.triggered;
        bool dashPressed = dashAction != null && dashAction.triggered;
        bool holdingDown = moveInput.y < -0.5f;

        // ------------------------------
        // JUMP BUFFER
        // ------------------------------
        if (jumpPressed)
            jumpBufferTimer = jumpBufferTime;

        // ------------------------------
        // DASH INPUT
        // ------------------------------
        if (dashPressed)
        {
            float dir = moveInput.x != 0 ? Mathf.Sign(moveInput.x) : 1f;
            StartDash(dir);
        }

        // ------------------------------
        // WAVE-DASH QUEUE (jump + holding down)
        // ------------------------------
        if (jumpPressed && holdingDown)
        {
            waveDashQueued = true;

            var main = trailParticles.main;
            main.startColor = Color.cyan;

            waveDashTimer = waveDashInputWindow;
        }
        else
        {
            waveDashTimer -= Time.deltaTime;
            if (waveDashTimer <= 0f)
                waveDashQueued = false;
        }

        // ------------------------------
        // TIMERS
        // ------------------------------
        if (coyoteTimer > 0f) coyoteTimer -= Time.deltaTime;
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;

        // ------------------------------
        // FINAL JUMP EXECUTION CONDITIONS
        // ------------------------------
        if (jumpBufferTimer > 0f &&
            (coyoteTimer > 0f || availableJumps > 0) &&
            Time.time - lastJumpTime > jumpCooldown)
        {
            jumpRequestedThisFrame = true;
            jumpBufferTimer = 0f;
        }
    }

    #endregion

    #region Movement Physics

    private void HandleMovementPhysics()
    {
        float control = state == PlayerState.Grounded ? 1f : airControlMultiplier;
        Vector2 vel = rb.linearVelocity;
        vel.x = moveInput.x * moveSpeed * control;
        rb.linearVelocity = vel;
    }

    private void HandleGravityPhysics()
    {
        float vy = rb.linearVelocity.y;

        if (vy > 0.01f)
        {
            bool holdingJump = jumpAction != null && jumpAction.ReadValue<float>() > 0.5f;
            rb.gravityScale = holdingJump ? upwardGravity : jumpCutGravity;
        }
        else if (vy < -0.01f)
        {
            rb.gravityScale = downwardGravity;
        }
        else rb.gravityScale = 1f;

        if (rb.linearVelocity.y < -terminalVelocity)
        {
            Vector2 v = rb.linearVelocity;
            v.y = -terminalVelocity;
            rb.linearVelocity = v;
        }
    }

    #endregion

    #region Jumping

    private void ExecuteJump()
    {
        if (Time.time - lastJumpTime < jumpCooldown) return;

        bool allowed = (coyoteTimer > 0f) || (availableJumps > 0);
        if (!allowed) return;

        lastJumpTime = Time.time;

        if (state != PlayerState.Grounded) availableJumps = Mathf.Max(0, availableJumps - 1);
        else availableJumps = Mathf.Max(0, availableJumps - 1);

        Vector2 v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        state = PlayerState.Jumping;

        if (availableJumps == 0 && balance != null)
        {
            if (rotationCoroutine != null) StopCoroutine(rotationCoroutine);
            rotationCoroutine = StartCoroutine(DoDoubleJumpRotation(0.3f));
        }
    }

    private IEnumerator DoDoubleJumpRotation(float duration)
    {
        if (balance == null) yield break;

        float prevSmooth = balance.smoothSpeed;
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

        yield return new WaitForSeconds(0.15f);

        if (joint != null) joint.enabled = false;
        balance.smoothSpeed = prevSmooth;
        balance.targetRotation = 0f;
    }

    #endregion

    #region Dash & Wave Dash

    private void StartDash(float direction)
    {
        var trailParticlesMain = trailParticles.main;
        trailParticlesMain.startColor = Color.cyan;
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
        var trailParticlesMain = trailParticles.main;
        dashTime += Time.fixedDeltaTime;
        float t = dashTime / dashDuration;
        rb.linearVelocity = Vector2.Lerp(dashStartVel, dashTargetVel, t);

        if (headRb != null)
        {
            headRb.position += (rb.linearVelocity - dashStartVel) * Time.fixedDeltaTime;
        }
        if (t >= 1f)
        {
            trailParticlesMain.startColor = Color.white;
            isDashing = false;
            rb.gravityScale = 1f;
            RestoreDashCollisions();
        }
    }

    private void HandleWaveDashPhysics()
    {
        if (isWaveDashing)
        {
            var trailParticlesMain = trailParticles.main;
            waveDashTime += Time.fixedDeltaTime;
            float t = waveDashTime / waveDashDuration;
            rb.linearVelocity = Vector2.Lerp(waveDashStartVel, waveDashTargetVel, t);

            if (t >= 1f)
            {
                trailParticlesMain.startColor = Color.white;
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

    #endregion

    #region Ground Detection

    private void UpdateGroundedState()
    {
        bool grounded = false;
        Vector2 checkCenter = (Vector2)transform.position + groundCheckOffset;
        Collider2D[] hits = Physics2D.OverlapCircleAll(checkCenter, groundCheckRadius, groundLayers);

        foreach (var c in hits)
        {
            RaycastHit2D hit = Physics2D.Raycast((Vector2)transform.position + Vector2.up * 0.1f,
                (c.transform.position - transform.position).normalized, 1f, groundLayers);

            if ((hit.collider != null && Vector2.Angle(hit.normal, Vector2.up) <= maxGroundAngle) ||
                c.bounds.center.y <= transform.position.y)
            {
                grounded = true;
                break;
            }
        }

        if (grounded)
        {
            availableJumps = maxJumpCount;
            coyoteTimer = coyoteTime;
            if (state == PlayerState.Falling || state == PlayerState.Jumping)
                state = PlayerState.Grounded;
        }
        else if (state == PlayerState.Grounded)
        {
            coyoteTimer = coyoteTime;
            state = PlayerState.Falling;
        }
    }

    #endregion

    #region Death Handling

    public IEnumerator HandleDeath()
    {
        if (isDead) yield break;

        isDead = true;
        state = PlayerState.Dead;

        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Dynamic;

        if (headRb != null)
        {
            var spring = headRb.GetComponent<SpringJoint2D>();
            if (spring != null) spring.enabled = false;
        }

        yield return new WaitForSeconds(0.5f);
        GameManager.isGameOver = true;
    }

    #endregion
}
