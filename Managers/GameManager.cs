using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    public static bool isGameOver = false;
    public static int playerMeleeWeapons = 0;

    void Awake()
    {
        Time.timeScale = 1f;
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (isGameOver)
        {
            Time.timeScale = 1f;
            isGameOver = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
