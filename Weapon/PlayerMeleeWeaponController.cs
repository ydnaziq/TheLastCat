using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody2D)), RequireComponent(typeof(Balance)), RequireComponent(typeof(Collider2D))]
public class PlayerMeleeWeaponController : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform target;
    public float followStrength = 8f;
    public float damping = 5f;
    public float maxSpeed = 12f;
    public float lookaheadInertia = 0.2f;

    [Header("Equip Settings")]
    private InputAction equipAction;
    public float equipDistance = 1.5f;
    private bool equip = false;

    private Rigidbody2D rb;
    private Vector2 velocity;

    [Header("Slash Settings")]
    private int weaponNumber = 0;
    private int slashNumber = 0;
    private int slashLimit = 1;
    private InputAction slashAction;
    private bool isSlashing = false;
    public float slashDistance = 10f;
    public float slashDuration = 0.2f;

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

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        player = GameObject.FindGameObjectWithTag("Player").transform;

        equipAction = InputSystem.actions.FindAction("Interact");
        moveAction = InputSystem.actions.FindAction("Move");
        slashAction = InputSystem.actions.FindAction("Slash");

        enemyLayerMask = 1 << LayerMask.NameToLayer("Enemy");
        fatalLayerMask = 1 << LayerMask.NameToLayer("Fatal");

        OutlineMaterial = Resources.Load<Material>("Materials/OutlineMaterial");
        DefaultMaterial = Resources.Load<Material>("Materials/DefaultMaterial");
        mostRecentTarget = FindNearestTargets().nearestEnemy;

        cinemachineCollisionImpulseSource = GetComponent<CinemachineCollisionImpulseSource>();
        cinemachineCollisionImpulseSource.enabled = false;
    }

    void Update()
    {
        slashLimit = GameManager.playerMeleeWeapons + 1;
        moveInput = moveAction.ReadValue<Vector2>();
        float distance = Vector2.Distance(player.position, transform.position);

        if (equipAction.triggered && distance < equipDistance)
        {
            Debug.Log("Toggling equip:" + !equip);
            equip = !equip;

            Collider2D col = GetComponent<Collider2D>();
            if (equip)
            {
                GameManager.playerMeleeWeapons += 1;
                weaponNumber = GameManager.playerMeleeWeapons;
                cinemachineCollisionImpulseSource.enabled = true;
                gameObject.layer = LayerMask.NameToLayer("PlayerWeapon");
                int playerLayer = LayerMask.NameToLayer("Player");
                col.excludeLayers = playerLayer >= 0 ? 1 << playerLayer : 0;
            }
            else
            {
                GameManager.playerMeleeWeapons -= 1;
                weaponNumber = 0;
                cinemachineCollisionImpulseSource.enabled = false;
                gameObject.layer = LayerMask.NameToLayer("Default");
                int nothingLayer = LayerMask.NameToLayer("Nothing");
                col.excludeLayers = nothingLayer >= 0 ? 1 << nothingLayer : 0;
            }
            target = equip ? player : null;
        }

        if (slashAction.triggered && equip && !isSlashing)
        {
            slashNumber += 1;
            if (slashNumber == slashLimit)
            {
                slashNumber = 1;
            }
            if (slashNumber == weaponNumber)
            {
                StartCoroutine(SlashCoroutine());
            }
            Debug.Log("slash number " + slashNumber + " slash limit " + slashLimit + " weapon number" + weaponNumber);
        }
    }

    void FixedUpdate()
    {
        if (equip)
        {
            if (!isSlashing)
            {
                FollowAndTrack();
            }
        }
    }

    void FollowAndTrack()
    {
        Vector2 predicted = target.position;

        Vector2 toTarget = predicted - rb.position;
        Vector2 desiredVelocity = toTarget * followStrength;

        velocity = Vector2.Lerp(
            velocity,
            desiredVelocity,
            1f - Mathf.Exp(-damping * Time.fixedDeltaTime)
        );

        velocity = Vector2.ClampMagnitude(velocity, maxSpeed);

        var n = FindNearestTargets();
        float angle;
        if (n.nearestOverall != null)
        {
            Vector2 d = (n.nearestOverall.position - transform.position).normalized;
            angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
        }
        else
        {
            angle = moveInput.x >= 0 ? 90f : 270f;
        }

        var bal = GetComponent<Balance>();
        bal.smoothSpeed = 10000f;
        bal.targetRotation = angle;

        if (mostRecentTarget != n.nearestEnemy && n.nearestEnemy != null && mostRecentTarget != null)
        {
            mostRecentTarget.GetComponent<SpriteRenderer>().material = DefaultMaterial;
            n.nearestEnemy.GetComponent<SpriteRenderer>().material = OutlineMaterial;
            mostRecentTarget = n.nearestEnemy;
        }

        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);
    }

    IEnumerator SlashCoroutine()
    {
        isSlashing = true;
        Transform sword = transform;

        Transform nearest = FindNearestTargets().nearestEnemy;

        Vector2 dir = nearest != null
            ? (nearest.position - player.position).normalized
            : moveInput.normalized;

        if (dir == Vector2.zero)
            dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

        Vector2 stabTarget = (Vector2)player.position + dir * slashDistance;

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
