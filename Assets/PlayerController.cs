using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header ("Horizontal Movement Settings")]
    private Rigidbody2D rb;
[SerializeField] private float walkspeed = 5;//7
    private float xAxis;

    [Header ("Vertical Movement Settings")]
    [SerializeField] private float jumpForce = 15;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.4f;
    [SerializeField] private int maxJumps = 2; // 2 = normal jump + one double jump
    private int jumpsRemaining;
    private bool isGrounded;

    public static PlayerController Instance;


    private void Awake()
    {
        if (Instance != null&& Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance=this;
        }
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        jumpsRemaining = maxJumps;
    }

    // Update is called once per frame
    void Update()
    {
        GetInputs();
        Move();
        Jump();
    }
    void GetInputs()
    {
        xAxis = Input.GetAxisRaw("Horizontal");
    }

    private void Move()
    {
           rb.linearVelocity = new Vector2(xAxis * walkspeed, rb.linearVelocity.y);
    }

    private void Jump()
    {
        // Refill jumps once we've landed (and aren't still moving up from a jump)
        if (isGrounded && rb.linearVelocity.y <= 0.01f)
        {
            jumpsRemaining = maxJumps;
        }

        if (Input.GetButtonUp("Jump") && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpCutMultiplier);
        }

        if (Input.GetButtonDown("Jump") && jumpsRemaining > 0)
        {
            // Walked off a ledge without jumping: only the air jump is left
            if (!isGrounded && jumpsRemaining == maxJumps)
            {
                jumpsRemaining--;
            }

            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpsRemaining--;
        }
    }

    // Count as grounded when touching something below us (not walls or ceilings)
    private void OnCollisionStay2D(Collision2D collision)
    {
        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
                return;
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
    }
}
