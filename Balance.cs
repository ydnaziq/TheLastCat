using UnityEngine;

public class Balance : MonoBehaviour
{
    private Rigidbody2D rb;
    public float targetRotation = 0f;
    public float smoothSpeed = 10f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        float newRotation = Mathf.LerpAngle(rb.rotation, targetRotation, smoothSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(newRotation);
    }
}
