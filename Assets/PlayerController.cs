using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header ("Horizontal Movement Settings")]
    private Rigidbody2D rb;
[SerializeField] private float walkspeed = 5;//7
    private float xAxis;
    Animator anim;
    SpriteRenderer sr;

    [Header ("Vertical Movement Settings")]
    [SerializeField] private float jumpForce = 15;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.4f;
    [SerializeField] private int maxJumps = 2; // 2 = normal jump + one double jump
    private int jumpsRemaining;
    private bool isGrounded;
    private bool isDoubleJumping;
    private ContactFilter2D groundFilter;

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
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.freezeRotation = true; // keep the player upright on collisions
        jumpsRemaining = maxJumps;
        anim = GetComponent<Animator>();
        sr = GetComponentInChildren<SpriteRenderer>();

        // Only contacts whose normal points up (within 45 degrees) count as ground
        groundFilter.useTriggers = false;
        groundFilter.SetNormalAngle(45f, 135f);
    }

    // Update is called once per frame
    void Update()
    {
        GetInputs();
        CheckGrounded();
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
           anim.SetBool("Walking", rb.linearVelocity.x != 0 && isGrounded);
           Flip();
    }

    // Face the direction of input; keep the last facing when there's no input
    private void Flip()
    {
        if (xAxis > 0)
        {
            sr.flipX = true;
        }
        else if (xAxis < 0)
        {
            sr.flipX = false;
        }
    }

    private void Jump()
    {
        // Refill jumps once we've landed (and aren't still moving up from a jump)
        if (isGrounded && rb.linearVelocity.y <= 0.01f)
        {
            jumpsRemaining = maxJumps;
            isDoubleJumping = false;
        }

        if (Input.GetButtonUp("Jump") && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpCutMultiplier);
        }

        bool startedDoubleJump = false;

        if (Input.GetButtonDown("Jump") && jumpsRemaining > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpsRemaining--;

            // Only the jump after the first one has been used counts as a double jump
            if (jumpsRemaining < maxJumps - 1)
            {
                isDoubleJumping = true;
                startedDoubleJump = true;
            }
        }

        anim.SetBool("Jumping", !isGrounded && !isDoubleJumping);
        anim.SetBool("DoubleJumping", isDoubleJumping);

        // Start the double jump animation from its first frame on the exact frame of the input
        if (startedDoubleJump)
        {
            // State may be named after the clip if the controller wasn't built by PlayerAnimatorSetup
            int doubleJumpState = anim.HasState(0, Animator.StringToHash("DoubleJump"))
                ? Animator.StringToHash("DoubleJump")
                : Animator.StringToHash("Player_Double_Jump");
            anim.Play(doubleJumpState, 0, 0f);
            anim.Update(0f);
        }
    }

    // Count as grounded when touching something below us (not walls or ceilings).
    // Checked every frame so leaving a wall doesn't clear grounded while still on the floor.
    private void CheckGrounded()
    {
        isGrounded = rb.IsTouching(groundFilter);
    }
}
