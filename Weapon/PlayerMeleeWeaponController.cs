using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody2D)), RequireComponent(typeof(Balance)), RequireComponent(typeof(Collider2D))]
public class PlayerMeleeWeaponController : MonoBehaviour
{
    [Header("Follow Settings")]
    private float angle;
    public Transform target;
    public float followStrength = 8f;
    public float damping = 5f;
    public float maxSpeed = 12f;
    public float lookaheadInertia = 0.2f;

    [Header("Equip Settings")]
    public bool IsEquipped;
    public void OnEquipped() { IsEquipped = true; }
    public void OnUnequipped() { IsEquipped = false; }

    private Rigidbody2D rb;
    private Vector2 velocity;

    [Header("Slash Settings")]
    private bool isSlashing = false;
    public float slashDistance = 10f;
    public float slashDuration = 0.2f;

    [Header("Animation Settings")]
    [HideInInspector] public Vector3 originalScale;
    [HideInInspector] public Quaternion originalRotation;

    private Transform player;
    private InputAction moveAction;
    private Vector2 moveInput;

    private Transform mostRecentTarget;
    private Material OutlineMaterial;
    private Material DefaultMaterial;

    private static readonly Collider2D[] hits = new Collider2D[100];
    private LayerMask enemyLayerMask;
    private LayerMask fatalLayerMask;

    private CinemachineCollisionImpulseSource cinemachineCollisionImpulseSource;

    [Header("Aim / Input Settings")]
    [Tooltip("Ignore joystick input whose magnitude is below this value (circular deadzone).")]
    [Range(0f, 0.5f)]
    public float stickDeadzone = 0.18f;

    [Tooltip("If true, the player must have stick neutral (inside deadzone) for auto-targeting to take effect.")]
    public bool requireStickNeutralToAutoTarget = true;

    [Tooltip("How quickly to lerp the drawn/visual angle when using stick aiming (higher = faster).")]
    public float angleLerpSpeed = 12f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        // Safely grab the Move action
        var asset = InputSystem.actions;
        if (asset != null)
            moveAction = asset.FindAction("Move");

        enemyLayerMask = 1 << LayerMask.NameToLayer("Enemy");
        fatalLayerMask = 1 << LayerMask.NameToLayer("Fatal");

        OutlineMaterial = Resources.Load<Material>("Materials/OutlineMaterial");
        DefaultMaterial = Resources.Load<Material>("Materials/DefaultMaterial");

        mostRecentTarget = FindNearestTargets().nearestEnemy;

        cinemachineCollisionImpulseSource = GetComponent<CinemachineCollisionImpulseSource>();
        if (cinemachineCollisionImpulseSource != null)
            cinemachineCollisionImpulseSource.enabled = false;

        originalScale = transform.localScale;
        originalRotation = transform.localRotation;
    }

    void Update()
    {
        if (moveAction != null)
            moveInput = moveAction.ReadValue<Vector2>();
        else
            moveInput = Vector2.zero;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            if (IsEquipped)
            {
                if (cinemachineCollisionImpulseSource != null)
                    cinemachineCollisionImpulseSource.enabled = true;

                gameObject.layer = LayerMask.NameToLayer("PlayerWeapon");
                int playerLayer = LayerMask.NameToLayer("Player");
                // original code set excludeLayers — keep that behavior if supported
#if UNITY_EDITOR || UNITY_STANDALONE
                try
                {
                    col.excludeLayers = playerLayer >= 0 ? 1 << playerLayer : 0;
                }
                catch { /* some Collider2D types may not expose excludeLayers in all Unity versions - ignore if not present */ }
#endif
            }
            else
            {
                if (cinemachineCollisionImpulseSource != null)
                    cinemachineCollisionImpulseSource.enabled = false;

                gameObject.layer = LayerMask.NameToLayer("Default");
                int nothingLayer = LayerMask.NameToLayer("Nothing");
#if UNITY_EDITOR || UNITY_STANDALONE
                try
                {
                    col.excludeLayers = nothingLayer >= 0 ? 1 << nothingLayer : 0;
                }
                catch { }
#endif
            }
        }

        target = IsEquipped ? player : null;
    }

    void FixedUpdate()
    {
        if (IsEquipped)
        {
            if (!isSlashing)
            {
                FollowAndTrack();
            }
        }
    }

    void FollowAndTrack()
    {
        if (target == null) return;

        // Physics following velocity setup
        Vector2 predicted = target.position;
        Vector2 toTarget = predicted - rb.position;
        Vector2 desiredVelocity = toTarget * followStrength;

        velocity = Vector2.Lerp(
            velocity,
            desiredVelocity,
            1f - Mathf.Exp(-damping * Time.fixedDeltaTime)
        );

        velocity = Vector2.ClampMagnitude(velocity, maxSpeed);

        // Acquire nearby targets
        var n = FindNearestTargets();

        // If the stick is moved beyond deadzone, treat as active aiming
        bool stickActive = moveInput.magnitude >= stickDeadzone;

        // Option: when stick is active, do not auto-target
        Transform autoTarget = n.nearestOverall;
        if (requireStickNeutralToAutoTarget && stickActive)
            autoTarget = null;

        // Compute desired angle
        float targetAngle = angle; // default keep previous angle if nothing changes

        if (autoTarget != null)
        {
            Vector2 d = ((Vector2)autoTarget.position - (Vector2)transform.position).normalized;
            targetAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
        }
        else
        {
            // If stick is active, aim in stick direction
            if (stickActive)
            {
                Vector2 stickDir = moveInput.normalized;
                if (stickDir != Vector2.zero)
                    targetAngle = Mathf.Atan2(stickDir.y, stickDir.x) * Mathf.Rad2Deg + 90f;
            }
            else
            {
                // stick neutral and no auto target -> keep current angle (no change)
                // targetAngle = angle;
            }
        }

        // Smooth the visual angle to targetAngle (optional)
        angle = Mathf.LerpAngle(angle, targetAngle, Mathf.Clamp01(Time.fixedDeltaTime * angleLerpSpeed));

        // Apply to Balance component (your code used it)
        var bal = GetComponent<Balance>();
        if (bal != null)
        {
            // if you want instant lock while following, adjust smoothSpeed accordingly
            bal.smoothSpeed = 10000f;
            bal.targetRotation = angle;
        }

        // Handle material outline swaps only when nearestEnemy changes
        if (n.nearestEnemy != mostRecentTarget)
        {
            // Remove outline from previous
            if (mostRecentTarget != null)
            {
                var srOld = mostRecentTarget.GetComponent<SpriteRenderer>();
                if (srOld != null && DefaultMaterial != null)
                    srOld.material = DefaultMaterial;
            }

            // Apply outline to new
            if (n.nearestEnemy != null)
            {
                var srNew = n.nearestEnemy.GetComponent<SpriteRenderer>();
                if (srNew != null && OutlineMaterial != null)
                    srNew.material = OutlineMaterial;
            }

            mostRecentTarget = n.nearestEnemy;
        }

        // Move the rigidbody
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);
    }

    public IEnumerator SlashRoutine()
    {
        isSlashing = true;
        Transform sword = transform;

        Transform nearest = FindNearestTargets().nearestEnemy;

        // If stick is active and we have no nearest target, use stick direction, else use fallback
        Vector2 dir = Vector2.zero;
        if (nearest != null)
        {
            dir = (nearest.position - player.position).normalized;
        }
        else if (moveInput.magnitude >= stickDeadzone)
        {
            dir = moveInput.normalized;
        }
        else
        {
            // fallback to current forward (from angle), or right if angle invalid
            float rad = (angle + 90f) * Mathf.Deg2Rad; // invert earlier -90f
            dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (dir == Vector2.zero) dir = Vector2.right;
        }

        if (dir == Vector2.zero)
            dir = Vector2.right;

        float stabAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

        Vector2 stabTarget = (Vector2)player.position + dir * slashDistance;

        // DOTween movement coroutine - moves the rigidbody over time
        yield return DOTween.To(
            () => rb.position,
            p => rb.MovePosition(p),
            stabTarget,
            slashDuration
        ).SetEase(Ease.OutQuad).WaitForCompletion();

        isSlashing = false;
    }

    private (Transform nearestEnemy, Transform nearestOverall) FindNearestTargets()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(enemyLayerMask | fatalLayerMask);

        int count = Physics2D.OverlapCircle(
            transform.position,
            40f,
            filter,
            hits
        );

        Transform nearestEnemy = null;
        Transform nearestOverall = null;
        float minDistEnemy = float.MaxValue;
        float minDistOverall = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            Transform t = hits[i].transform;
            float distSqr = (t.position - transform.position).sqrMagnitude;
            int layer = t.gameObject.layer;

            // Check nearest enemy
            if (((1 << layer) & enemyLayerMask) != 0)
            {
                if (distSqr < minDistEnemy)
                {
                    minDistEnemy = distSqr;
                    nearestEnemy = t;
                }
            }

            // Check nearest overall (enemy or fatal)
            if (distSqr < minDistOverall)
            {
                minDistOverall = distSqr;
                nearestOverall = t;
            }
        }

        return (nearestEnemy, nearestOverall);
    }
}
