using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CircleCollider2D))]
public class Goal : MonoBehaviour
{
    private void Awake()
    {
        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        circle.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            int currentIndex = SceneManager.GetActiveScene().buildIndex;
            SceneManager.LoadScene(currentIndex + 1);
        }
    }
}