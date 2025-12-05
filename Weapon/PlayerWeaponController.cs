using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody2D), typeof(Balance), typeof(Collider2D))]
public class PlayerWeaponController : MonoBehaviour
{
    public enum WeaponState { Idle, Orbit, Following, Attacking }

    #region General Settings
    [Header("General Settings")]

    Collider2D col;
    public bool IsEquipped;
    private Transform player;
    private Transform target;
    public float followStrength = 8f;
    public float damping = 5f;
    public float maxSpeed = 12f;
    private float lastMoveX = 1f;

    [Header("Projectile Settings")]
    public float projectileForce = 10f;
    public float projectileDuration = 0.2f;

    [Header("Animation / Transform Settings")]
    [HideInInspector] public Vector3 originalScale;
    [HideInInspector] public Quaternion originalRotation;

    [Header("Aim / Input Settings")]
    [SerializeField] private float followRadius = 2f;
    [SerializeField] private float aimBias = 0.25f;
    [Range(0f, 0.5f)] public float stickDeadzone = 0.18f;
    public bool requireStickNeutralToAutoTarget = true;
    public float angleLerpSpeed = 12f;

    [Header("Orbit Settings")]
    [SerializeField] private float orbitDistance = 1.5f;   // distance from player
    [SerializeField] private float orbitSpeed = 90f;       // degrees per second
    [SerializeField] private float orbitTime = 1.4f;       // for DOTween ease
    private float orbitAngle = 0f;
    private Tween orbitTween;

    private Rigidbody2D rb;
    private Balance bal;
    private InputAction moveAction;
    private CinemachineCollisionImpulseSource cinemachineCollisionImpulseSource;
    private PlayerWeaponsManager playerWeaponsManager;
    private PlayerController playerController;
    #endregion

    #region Runtime Variables
    private Vector2 moveInput;
    private Vector2 velocity;
    private float angle;
    private bool isFiring = false;
    public bool onCooldown = false;
    private float coolDownDistance = 2f;

    private Transform mostRecentTarget;
    private Material OutlineMaterial;
    private Material DefaultMaterial;

    private LayerMask enemyLayerMask;
    private LayerMask fatalLayerMask;
    private static readonly Collider2D[] hits = new Collider2D[100];

    public WeaponState currentState = WeaponState.Idle;
    #endregion

    #region Unity Callbacks
    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        bal = GetComponent<Balance>();
        col = GetComponent<Collider2D>();
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
        playerController = player.GetComponent<PlayerController>();
    }

    private void Update()
    {
        ReadInput();
        UpdateLayerAndTarget();
        UpdateState();
    }

    private void FixedUpdate()
    {
        if (!IsEquipped) return;

        HandleStateLogic();

        if (onCooldown && Vector2.Distance(rb.position, player.position) <= coolDownDistance)
            onCooldown = false;
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

    #region State Machine
    private void UpdateState()
    {
        if (isFiring)
            currentState = WeaponState.Attacking;
        else if (playerController.state == PlayerController.PlayerState.Idle && FindNearestTargets().nearestEnemy == null)
        {
            currentState = WeaponState.Orbit;
        }
        else
            currentState = WeaponState.Following;
    }

    private void HandleStateLogic()
    {
        switch (currentState)
        {
            case WeaponState.Following:
                col.enabled = false;
                StopOrbitAnimation();
                FollowLogic();
                break;
            case WeaponState.Orbit:
                col.enabled = false;
                StartOrbitAnimation();
                OrbitLogic();
                break;
            case WeaponState.Attacking:
                col.enabled = true;
                StopOrbitAnimation();
                break;
            case WeaponState.Idle:
            default:
                StopOrbitAnimation();
                break;
        }
    }
    #endregion

    #region Movement Logic
    private void FollowLogic()
    {
        if (target == null) return;

        Vector2 currentPos = rb.position;
        Vector2 playerPos = target.position;

        Vector2 followDir = (playerPos - currentPos).normalized;

        Transform autoTarget = FindNearestTargets().nearestOverall;
        bool stickActive = moveInput.magnitude >= stickDeadzone;
        if (requireStickNeutralToAutoTarget & stickActive) autoTarget = null;

        Vector2 moveDir = followDir;

        if (autoTarget != null)
        {
            Vector2 toEnemy = ((Vector2)autoTarget.position - currentPos).normalized;
            moveDir = Vector2.Lerp(followDir, toEnemy, aimBias).normalized;
        }

        Vector2 desiredVelocity = moveDir * followStrength;
        float smoothFactor = 1f - Mathf.Exp(-damping * Time.fixedDeltaTime);
        velocity = Vector2.Lerp(velocity, desiredVelocity, smoothFactor);

        Vector2 desiredPos = currentPos + velocity * Time.fixedDeltaTime;

        Vector2 offset = desiredPos - playerPos;
        if (offset.magnitude > followRadius)
            desiredPos = playerPos + offset.normalized * followRadius;

        rb.MovePosition(desiredPos);

        UpdateRotation(autoTarget, stickActive);
        UpdateTargetOutline(FindNearestTargets().nearestEnemy);
    }

    private void UpdateRotation(Transform autoTarget, bool stickActive)
    {
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
    }
    #endregion

    #region Targeting
    private void UpdateTargetOutline(Transform nearestEnemy)
    {
        if (nearestEnemy != mostRecentTarget)
        {
            if (mostRecentTarget != null)
            {
                var srOld = mostRecentTarget.GetComponent<SpriteRenderer>();
                if (srOld != null && DefaultMaterial != null)
                    srOld.material = DefaultMaterial;
            }

            if (nearestEnemy != null)
            {
                var srNew = nearestEnemy.GetComponent<SpriteRenderer>();
                if (srNew != null && OutlineMaterial != null)
                    srNew.material = OutlineMaterial;
            }

            mostRecentTarget = nearestEnemy;
        }
    }

    private (Transform nearestEnemy, Transform nearestOverall) FindNearestTargets()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.SetLayerMask(enemyLayerMask | fatalLayerMask);

        int count = Physics2D.OverlapCircle(transform.position, 15f, filter, hits);

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

    private Vector2 GetAttackDirection()
    {
        Transform nearest = FindNearestTargets().nearestEnemy;
        if (nearest != null)
            return (nearest.position - player.position).normalized;
        if (moveInput.magnitude >= stickDeadzone)
            return moveInput.normalized;
        return new Vector2(lastMoveX, 0f);
    }
    #endregion

    #region Attack Routines
    public IEnumerator ProjectileRoutine()
    {
        isFiring = true;

        Vector2 dir = GetAttackDirection();
        bal.targetRotation = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 stabTarget = (Vector2)player.position + dir * projectileForce;

        yield return DOTween.To(() => rb.position, p => rb.MovePosition(p), stabTarget, projectileDuration)
                        .SetEase(Ease.OutQuad)
                        .WaitForCompletion();

        isFiring = false;
        onCooldown = true;
    }
    #endregion

    #region Orbit Animation

    private void OrbitLogic()
    {
        if (target == null) return;

        // Advance angle by orbitSpeed
        orbitAngle += orbitSpeed * Time.fixedDeltaTime;
        orbitAngle %= 360f; // keep angle in 0-360

        // Compute orbit position using cosine/sine
        Vector2 orbitOffset = new Vector2(Mathf.Cos(orbitAngle * Mathf.Deg2Rad),
                                          Mathf.Sin(orbitAngle * Mathf.Deg2Rad)) * orbitDistance;

        rb.MovePosition((Vector2)target.position + orbitOffset);

        // Keep rotation smooth
        if (bal != null)
        {
            bal.smoothSpeed = 100f;
            bal.targetRotation = angle;
        }
    }

    private void StartOrbitAnimation()
    {
        if (orbitTween != null && orbitTween.IsActive()) return;

        // Randomize starting angle for variation between knives
        orbitAngle = Random.Range(0f, 360f);

        // Use DOTween for subtle easing if desired
        orbitTween = DOTween.To(() => orbitAngle, x => orbitAngle = x, 360f + orbitAngle, orbitTime)
                            .SetEase(Ease.OutElastic)
                            .SetLoops(-1, LoopType.Incremental);
    }

    private void StopOrbitAnimation()
    {
        orbitTween?.Kill();
        orbitTween = null;
    }

    #endregion
}
