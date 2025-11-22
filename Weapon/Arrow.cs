using UnityEngine;

[RequireComponent(typeof(Rigidbody2D)), RequireComponent(typeof(Collider2D)), RequireComponent(typeof(TrailRenderer))]
public class Arrow : MonoBehaviour
{
    public Transform enemyTransform;
    private Rigidbody2D rb;
    private GameObject player;
    private GameObject block;
    private TrailRenderer trail;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        player = GameObject.FindGameObjectWithTag("PlayerHead");
        block = Resources.Load<GameObject>("Prefabs/Effects/Block");
        trail = GetComponent<TrailRenderer>();

        if (trail == null)
        {
            Debug.LogWarning("No TrailRenderer found on arrow.");
        }
    }

    void Update()
    {
        if (gameObject.layer == LayerMask.NameToLayer("Default"))
        {
            SetTrailGradient(Color.papayaWhip, Color.antiqueWhite);
        }
    }

    void SetTrailGradient(Color start, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
            new GradientColorKey(start, 0f),
            new GradientColorKey(end, 1f)
            },
            new GradientAlphaKey[] {
            new GradientAlphaKey(start.a, 0f),
            new GradientAlphaKey(end.a, 1f)
            }
        );

        trail.colorGradient = gradient;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Block") || collision.gameObject.layer == LayerMask.NameToLayer("PlayerWeapon"))
        {
            Instantiate(block, collision.contacts[0].point, Quaternion.identity);

            if (player != null && enemyTransform != null)
            {
                rb.linearVelocity = Vector2.zero;

                Vector2 toEnemy;
                if (enemyTransform != null)
                    toEnemy = ((Vector2)enemyTransform.position - rb.position).normalized;
                else
                    toEnemy = Random.insideUnitCircle.normalized;
                Vector2 force = toEnemy * 100f;
                rb.AddForce(force, ForceMode2D.Impulse);
            }

            if (trail != null)
            {
                trail.startColor = Color.white;
                trail.endColor = Color.white;
            }
        }
        else if (collision.gameObject.layer == LayerMask.NameToLayer("Default"))
        {
            gameObject.layer = LayerMask.NameToLayer("Default");
        }
    }
}