using UnityEngine;

public class KillEffect : MonoBehaviour
{
    public float duration = 1f;

    void Start()
    {
        Destroy(gameObject, duration);
    }
}
