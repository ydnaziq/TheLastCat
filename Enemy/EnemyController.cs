using UnityEngine;

[RequireComponent(typeof(Rigidbody2D)), RequireComponent(typeof(Collider2D))]
public class EnemyController : MonoBehaviour
{
    private enum State { Idle, Wander, Chase, Attack, Flee, Dead }

    [Header("References")]
    private Transform player;
    private Transform shootPoint;
    private GameObject arrowPrefab;
    private Rigidbody2D rb;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float fleeSpeed = 6f;
    public float wanderSpeed = 2f;
    public float smoothing = 0.1f;

    public float arrowSpeed = 15f;

    [Header("Ranges")]
    public float followDistance = 12f;
    public float attackDistance = 8f;
    public float fleeDistance = 2.5f;
    public float wanderRadius = 4f;
    public float wanderInterval = 2f;

    [Header("Attacking")]

    public float angleRandomness = 10f;
    private float gravity = 9.81f;
    public float attackCooldown = 1.2f;
    private State state = State.Idle;
    private float lastAttackTime = -99f;
    private float lastWanderTime = -99f;
    private Vector2 wanderTarget;
    private Vector2 smoothVelocity;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        shootPoint = transform.Find("ShootPoint");
        arrowPrefab = Resources.Load<GameObject>("Prefabs/Weapons/Arrow");

        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void Update()
    {
        if (player == null) return;

        float d = Vector2.Distance(transform.position, player.position);

        // State switching
        if (state == State.Dead) return;

        if (d <= fleeDistance)
        {
            if (state != State.Flee) state = State.Flee;
        }
        else if (d <= attackDistance)
        {
            if (state != State.Attack) state = State.Attack;
        }
        else if (d <= followDistance)
        {
            if (state != State.Chase) state = State.Chase;
        }
        else
        {
            if (state != State.Wander) EnterWander();
        }
    }

    void FixedUpdate()
    {
        switch (state)
        {
            case State.Idle:
                ApplyMovement(Vector2.zero);
                break;

            case State.Wander:
                UpdateWander();
                break;

            case State.Chase:
                ChasePlayer();
                break;

            case State.Attack:
                AttackPlayer();
                break;

            case State.Flee:
                FleeFromPlayer();
                break;
            case State.Dead:
                break;
        }
    }

    void EnterWander()
    {
        state = State.Wander;
        PickWanderTarget();
        lastWanderTime = Time.time;
    }

    void PickWanderTarget()
    {
        wanderTarget = (Vector2)transform.position + Random.insideUnitCircle * wanderRadius;
    }

    void UpdateWander()
    {
        if (Time.time - lastWanderTime > wanderInterval)
        {
            PickWanderTarget();
            lastWanderTime = Time.time;
        }

        Vector2 dir = (wanderTarget - (Vector2)transform.position).normalized;
        ApplyMovement(dir * wanderSpeed);

        if (Vector2.Distance(transform.position, wanderTarget) < 0.3f)
            ApplyMovement(Vector2.zero);
    }

    void ChasePlayer()
    {
        Vector2 dir = (player.position + new Vector3(0, 10, 0) - transform.position).normalized;
        ApplyMovement(dir * moveSpeed);
    }

    void FleeFromPlayer()
    {
        Vector2 dir = ((Vector2)transform.position - (Vector2)player.position).normalized;
        ApplyMovement(dir * fleeSpeed);
    }

    void AttackPlayer()
    {
        // Stop moving while shooting
        ApplyMovement(Vector2.zero);

        if (Time.time - lastAttackTime < attackCooldown)
            return;

        lastAttackTime = Time.time;

        LaunchArrow(player.position);
    }

    // -----------------------------
    //  MOVEMENT UTIL
    // -----------------------------
    void ApplyMovement(Vector2 desiredVel)
    {
        Vector2 smoothed = Vector2.SmoothDamp(rb.linearVelocity, desiredVel, ref smoothVelocity, smoothing);
        rb.linearVelocity = smoothed;
    }

    void LaunchArrow(Vector3 target)
    {
        if (arrowPrefab == null || shootPoint == null)
        {
            Debug.LogWarning("ArrowPrefab or ShootPoint not assigned.");
            return;
        }

        // Use positive gravity magnitude (g should be > 0)
        float g = Mathf.Abs(gravity);

        Vector3 startPos = shootPoint.position;
        Vector3 diff3 = target - startPos;

        // 2D: we assume X is horizontal and Y is vertical (Z ignored)
        float dxRaw = diff3.x;
        float dy = diff3.y;

        // keep track of horizontal direction sign
        float dir = Mathf.Sign(dxRaw);
        float dx = Mathf.Abs(dxRaw); // horizontal distance magnitude

        float v = arrowSpeed;
        float v2 = v * v;

        // handle almost-zero horizontal distance (shoot mostly vertical)
        if (dx < 1e-6f)
        {
            // If target is directly above/below, shoot straight up or down with full speed in Y
            float vy = Mathf.Clamp(dy > 0 ? v : -v, -v, v);
            Vector2 launchVel = new Vector2(0f, vy);

            // apply small angle randomness (in degrees)
            launchVel = Quaternion.Euler(0, 0, Random.Range(-angleRandomness, angleRandomness)) * launchVel;

            SpawnAndFire(startPos, launchVel);
            return;
        }

        // Quadratic in t = tan(theta): a t^2 + b t + c = 0
        // a = g*dx^2 / (2 v^2)
        // b = -dx
        // c = (g*dx^2) / (2 v^2) + dy
        float a = (g * dx * dx) / (2f * v2);
        float b = -dx;
        float c = (g * dx * dx) / (2f * v2) + dy;

        float discriminant = b * b - 4f * a * c;

        float t; // tan(theta)

        if (discriminant < 0f)
        {
            // Target unreachable at given speed.
            // Fallback strategy: aim at the target direction but with maximum achievable elevation (lower arc-ish)
            // Use the angle that gives maximal horizontal range (approx 45°) but oriented toward the target.
            t = 1f; // tan(45°) = 1 — a reasonable fallback
        }
        else
        {
            float sqrtD = Mathf.Sqrt(discriminant);

            // two solutions for t = tan(theta)
            float t1 = (-b + sqrtD) / (2f * a);
            float t2 = (-b - sqrtD) / (2f * a);

            // convert to angles so we can pick the lower arc (smaller absolute angle)
            float ang1 = Mathf.Atan(t1);
            float ang2 = Mathf.Atan(t2);

            // choose the smaller positive angle magnitude (lower arc). If one is negative, prefer positive upward shot.
            float chosenAngle = Mathf.Abs(ang1) < Mathf.Abs(ang2) ? ang1 : ang2;

            // If both angles produce downward launch (negative angle) but target is above, prefer the other if it exists.
            if (dy > 0f)
            {
                if (ang1 < 0f && ang2 > 0f) chosenAngle = ang2;
                else if (ang2 < 0f && ang1 > 0f) chosenAngle = ang1;
            }

            t = Mathf.Tan(chosenAngle);
        }

        // From tan θ = t, compute vx and vy components (vx positive magnitude)
        // vx = v / sqrt(1 + t^2); vy = vx * t
        float vxMag = v / Mathf.Sqrt(1f + t * t);
        float vySigned = vxMag * t;

        // restore horizontal direction sign
        float vxSigned = vxMag * dir;

        Vector2 launchVelocity = new Vector2(vxSigned, vySigned);

        // Add small angle randomness
        launchVelocity = Quaternion.Euler(0f, 0f, Random.Range(-angleRandomness, angleRandomness)) * launchVelocity;

        SpawnAndFire(startPos, launchVelocity);
    }

    void SpawnAndFire(Vector3 startPos, Vector2 launchVelocity)
    {
        GameObject arrow = Instantiate(arrowPrefab, startPos, Quaternion.identity);

        if (arrow.TryGetComponent<Rigidbody2D>(out Rigidbody2D rb))
        {
            rb.linearVelocity = launchVelocity;
        }
        else
        {
            Debug.LogError("Arrow prefab missing Rigidbody2D!");
        }

        if (arrow.TryGetComponent<Arrow>(out Arrow arrowScript))
        {
            arrowScript.enemyTransform = transform;
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("PlayerWeapon") && state != State.Dead)
        {
            Instantiate(Resources.Load<GameObject>("Prefabs/Effects/Blood"), transform.position, Quaternion.identity);
            GetComponent<SpriteFlash>().Flash(0.25f);
            GetComponent<ApplyKnockback>().Knockback(collision.transform);

            gameObject.tag = "Untagged";
            state = State.Dead;
            rb.gravityScale = 1f;
            GetComponent<Collider2D>().enabled = false;
        }
    }


    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, followDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackDistance);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, wanderRadius);
    }
}
