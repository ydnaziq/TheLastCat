using UnityEngine;

public class ApplyKnockback : MonoBehaviour
{
    public float knockbackForce = 50f;
    public float verticalBoost = 0.35f;

    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Knockback(Transform attacker)
    {
        Vector2 dir = transform.position - attacker.position;

        // Only left/right if you want
        dir.y = verticalBoost;

        dir = dir.normalized;

        rb.linearVelocity = Vector2.zero;
        rb.AddForce(new Vector2(dir.x, dir.y) * knockbackForce, ForceMode2D.Impulse);

        // cosmetic Z “kick”
        transform.position += new Vector3(0, 0, Random.Range(-0.1f, 0.1f));

    }
}
