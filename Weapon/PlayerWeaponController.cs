using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody2D), typeof(Balance), typeof(Collider2D))]
public class PlayerWeaponController : MonoBehaviour
{
    [Header("General Settings")]
    public bool IsEquipped;
    private Transform player;
    private Transform target;
    public float followStrength = 8f;
    public float damping = 5f;
    public float maxSpeed = 12f;
    public float lookaheadInertia = 0.2f;
    private float lastMoveX = 1f; // default facing right

    [Header("Projectile Settings")]
    public float projectileForce = 10f;
    public float projectileDuration = 0.2f;

    [Header("Melee Settings")]
    public float slashDuration = 0.1f;

    [Header("Animation / Transform Settings")]
    [HideInInspector] public Vector3 originalScale;
    [HideInInspector] public Quaternion originalRotation;

    [Header("Aim / Input Settings")]
    [SerializeField] private float followRadius = 2f;
    [SerializeField] private float aimBias = 0.25f;
    [Range(0f, 0.5f)] public float stickDeadzone = 0.18f;
    public bool requireStickNeutralToAutoTarget = true;
    public float angleLerpSpeed = 12f;

    private Rigidbody2D rb;
    private Balance bal;
    private InputAction moveAction;
    private Vector2 moveInput;
    private Vector2 velocity;
    private float angle;
    private bool isFiring = false;
    private bool isSlashing = false;
    public bool onCooldown = false;
    private float coolDownDistance = 2f;

    private Transform mostRecentTarget;
    private Material OutlineMaterial;
    private Material DefaultMaterial;

    private static readonly Collider2D[] hits = new Collider2D[100];
    private LayerMask enemyLayerMask;
    private LayerMask fatalLayerMask;
    private CinemachineCollisionImpulseSource cinemachineCollisionImpulseSource;
    private PlayerWeaponsManager playerWeaponsManager;

    #region Unity Callbacks
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        bal = GetComponent<Balance>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        moveAction = InputSystem.actions.FindAction("Move");

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

        playerWeaponsManager = player.GetComponent<PlayerWeaponsManager>();
    }

    void Update()
    {
        ReadInput();
        UpdateLayerAndTarget();
    }

    void FixedUpdate()
    {
        if (!IsEquipped) return;

        if (!isFiring && !isSlashing)
            FollowAndTrack();

        if (onCooldown)
        {
            float distToPlayer = Vector2.Distance(rb.position, player.position);
            if (distToPlayer <= coolDownDistance)
            {
                onCooldown = false;
            }
        }
    }
    #endregion

    #region Input & Equip
    private void ReadInput()
    {
        moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

        if (Mathf.Abs(moveInput.x) >= stickDeadzone)
            lastMoveX = Mathf.Sign(moveInput.x);
    }

    public void OnEquipped() => IsEquipped = true;
    public void OnUnequipped() => IsEquipped = false;

    private void UpdateLayerAndTarget()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;

        if (IsEquipped)
        {
            if (cinemachineCollisionImpulseSource != null) cinemachineCollisionImpulseSource.enabled = true;

            gameObject.layer = LayerMask.NameToLayer("PlayerWeapon");
            int playerLayer = LayerMask.NameToLayer("Player");
            col.excludeLayers = playerLayer >= 0 ? 1 << playerLayer : 0;
        }
        else
        {
            if (cinemachineCollisionImpulseSource != null) cinemachineCollisionImpulseSource.enabled = false;

            gameObject.layer = LayerMask.NameToLayer("Default");
            int nothingLayer = LayerMask.NameToLayer("Nothing");
            col.excludeLayers = nothingLayer >= 0 ? 1 << nothingLayer : 0;
        }

        target = IsEquipped ? player : null;
    }
    #endregion

    #region Follow & Aim
    private void FollowAndTrack()
    {
        if (target == null) return;
        if (isFiring || isSlashing) return;

        Vector2 playerPos = target.position;
        Vector2 currentPos = rb.position;

        // Base direction toward player
        Vector2 toPlayer = playerPos - currentPos;
        Vector2 followDir = toPlayer.normalized;

        // Nearest enemy
        Transform autoTarget = FindNearestTargets().nearestOverall;
        bool stickActive = moveInput.magnitude >= stickDeadzone;
        if (requireStickNeutralToAutoTarget & stickActive) autoTarget = null;

        Vector2 moveDir = followDir;

        if (autoTarget != null)
        {
            Vector2 toEnemy = ((Vector2)autoTarget.position - currentPos).normalized;
            moveDir = Vector2.Lerp(followDir, toEnemy, aimBias).normalized;
        }

        // Desired velocity
        Vector2 desiredVelocity = moveDir * followStrength;

        // Smooth velocity
        float smoothFactor = 1f - Mathf.Exp(-damping * Time.fixedDeltaTime);
        velocity = Vector2.Lerp(velocity, desiredVelocity, smoothFactor);

        // Calculate next position
        Vector2 desiredPos = currentPos + velocity * Time.fixedDeltaTime;

        // --- Soft radius constraint ---
        Vector2 offsetFromPlayer = desiredPos - playerPos;
        float offsetMag = offsetFromPlayer.magnitude;

        if (offsetMag > followRadius)
        {
            // Reduce the movement so it does not leave the radius
            // Soft approach: scale down velocity proportionally
            float scale = followRadius / offsetMag;
            desiredPos = playerPos + offsetFromPlayer * scale;
        }

        rb.MovePosition(desiredPos);

        // Rotation (smooth)
        float targetAngle = angle;
        if (autoTarget != null)
        {
            Vector2 d = ((Vector2)autoTarget.position - (Vector2)transform.position).normalized;
            targetAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
        else if (stickActive)
        {
            Vector2 stickDir = moveInput.normalized;
            if (stickDir != Vector2.zero)
                targetAngle = Mathf.Atan2(stickDir.y, stickDir.x) * Mathf.Rad2Deg - 180f;
        }

        angle = Mathf.LerpAngle(angle, targetAngle, Mathf.Clamp01(Time.fixedDeltaTime * angleLerpSpeed));

        if (bal != null)
        {
            bal.smoothSpeed = 10000f;
            bal.targetRotation = angle;
        }

        UpdateTargetOutline(FindNearestTargets().nearestEnemy);
    }

    private void UpdateTargetOutline(Transform nearestEnemy)
    {
        if (nearestEnemy != mostRecentTarget)
        {
            if (mostRecentTarget != null)
            {
                var srOld = mostRecentTarget.GetComponent<SpriteRenderer>();
                if (srOld != null && DefaultMaterial != null) srOld.material = DefaultMaterial;
            }

            if (nearestEnemy != null)
            {
                var srNew = nearestEnemy.GetComponent<SpriteRenderer>();
                if (srNew != null && OutlineMaterial != null) srNew.material = OutlineMaterial;
            }

            mostRecentTarget = nearestEnemy;
        }
    }
    #endregion

    #region Attack Routines
    public IEnumerator ProjectileRoutine()
    {
        isFiring = true;

        Vector2 dir = GetAttackDirection();
        bal.targetRotation = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 stabTarget = (Vector2)player.position + dir * projectileForce;

        yield return DOTween.To(
            () => rb.position,
            p => rb.MovePosition(p),
            stabTarget,
            projectileDuration
        ).SetEase(Ease.OutQuad).WaitForCompletion();

        isFiring = false;
        onCooldown = true;
    }

    public IEnumerator SlashRoutine()
    {
        onCooldown = true;
        isSlashing = true;
        yield return new WaitForSeconds(slashDuration);
        isSlashing = false;
        onCooldown = false;
    }

    #endregion

    #region Targeting

    private Vector2 GetAttackDirection()
    {

        Transform nearest = FindNearestTargets().nearestEnemy;
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
            // Stick neutral & no auto-target → use last known movement direction
            dir = new Vector2(lastMoveX, 0f);
        }


        return dir;
    }

    private (Transform nearestEnemy, Transform nearestOverall) FindNearestTargets()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(enemyLayerMask | fatalLayerMask);

        int count = Physics2D.OverlapCircle(transform.position, 40f, filter, hits);

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

            if (((1 << layer) & enemyLayerMask) != 0 && distSqr < minDistEnemy)
            {
                minDistEnemy = distSqr;
                nearestEnemy = t;
            }

            if (distSqr < minDistOverall)
            {
                minDistOverall = distSqr;
                nearestOverall = t;
            }
        }

        return (nearestEnemy, nearestOverall);
    }
    #endregion
}
