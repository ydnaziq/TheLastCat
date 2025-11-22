using UnityEngine;

public class PlayerHead : MonoBehaviour
{
    private PlayerController playerController;

    void Start()
    {
        Transform playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
        playerController = playerTransform.GetComponent<PlayerController>();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Fatal"))
        {
            Instantiate(Resources.Load<GameObject>("Prefabs/Effects/Blood"), transform.position, Quaternion.identity);
            playerController.StartCoroutine("HandleDeath");
            return;
        }
    }
}