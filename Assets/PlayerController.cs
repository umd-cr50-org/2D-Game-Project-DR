using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header ("Horizontal Movement Settings")]
    private Rigidbody2D rb;

[SerializeField] private float walkspeed = 5;

    private float xAxis;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    // Update is called once per frame
    void Update()
    {
        GetInputs();
        Move();
    }
    void GetInputs()
    {
        xAxis = Input.GetAxisRaw("Horizontal");
    }

    private void Move()
    {
           rb.linearVelocity = new Vector2(xAxis * walkspeed, rb.linearVelocity.y);
    }
}
