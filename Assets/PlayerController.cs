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

    [Header ("Dash Settings")]
    [SerializeField] private float dashDistance = 2f; // how far the warp carries the player, in units
    [SerializeField] private float dashWindup = 0.3f; // the "throw": player holds still this long before warping
    [SerializeField] private float dashTravelTime = 0.1f; // how long the warp takes to cover dashDistance
    [SerializeField] private float dashCooldown = 2f;
    [SerializeField] private float doubleTapWindow = 1.0f; // max time between taps
    private bool isDashing;
    private bool isDashMoving;
    private float dashDirection;
    private float dashMoveStartTime;
    private float dashStartX;
    private float nextDashTime;
    private float defaultGravityScale;
    private float lastTapTime = -1f;
    private float lastTapDirection;
    private float previousXAxis;

    [Header ("Vertical Movement Settings")]
    [SerializeField] private float jumpForce = 15;
    [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.4f;
    [SerializeField] private int maxJumps = 2; // 2 = normal jump + one double jump
    private int jumpsRemaining;
    private bool isGrounded;
    private bool isDoubleJumping;
    private ContactFilter2D groundFilter;

    private PlayerAttack attack;

    public static PlayerController Instance;
    public bool IsDashing => isDashing;
    private bool IsAttacking => attack != null && attack.IsAttacking;


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
        defaultGravityScale = rb.gravityScale;
        jumpsRemaining = maxJumps;
        anim = GetComponent<Animator>();
        sr = GetComponentInChildren<SpriteRenderer>();
        attack = GetComponent<PlayerAttack>();

        // Only contacts whose normal points up (within 45 degrees) count as ground
        groundFilter.useTriggers = false;
        groundFilter.SetNormalAngle(45f, 135f);
    }

    // Update is called once per frame
    void Update()
    {
        GetInputs();
        CheckGrounded();
        Dash();
        if (IsAttacking)
        {
            // Rooted in place for each hit, like a jab; gravity still applies
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else if (!isDashing)
        {
            Move();
            Jump();
        }
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

    // Double-tap left or right to warp that way: hold still for the throw, then shoot forward dashDistance.
    // Cooldown starts when the dash ends.
    private void Dash()
    {
        // A "tap" is the frame the horizontal input goes from released to pressed
        float tapDirection = 0f;
        if (xAxis != 0 && previousXAxis == 0)
        {
            tapDirection = Mathf.Sign(xAxis);
        }
        previousXAxis = xAxis;

        if (tapDirection != 0)
        {
            bool isDoubleTap = tapDirection == lastTapDirection
                && Time.time - lastTapTime <= doubleTapWindow;

            if (isDoubleTap && !isDashing && !IsAttacking && Time.time >= nextDashTime)
            {
                isDashing = true;
                isDashMoving = false;
                dashDirection = tapDirection;
                dashMoveStartTime = Time.time + dashWindup;
                rb.gravityScale = 0f; // hang in place for the throw and the warp
                sr.flipX = dashDirection > 0; // flipX is set when facing right
                anim.SetBool("Dashing", true);
                lastTapTime = -1f; // a third tap shouldn't count as another double tap
            }
            else
            {
                lastTapTime = Time.time;
                lastTapDirection = tapDirection;
            }
        }

        if (!isDashing)
        {
            return;
        }

        // Wind-up: the throw part of the animation, player stays put
        if (Time.time < dashMoveStartTime)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (!isDashMoving)
        {
            isDashMoving = true;
            dashStartX = rb.position.x;
        }

        // Stop once we've covered the distance, or if a wall has held us up for too long
        float remaining = dashDistance - Mathf.Abs(rb.position.x - dashStartX);
        bool blocked = Time.time >= dashMoveStartTime + dashTravelTime * 2f;
        if (remaining <= 0.01f || blocked)
        {
            EndDash();
            return;
        }

        // Slow down on the last physics step so we land on dashDistance instead of overshooting it
        float warpSpeed = dashDistance / dashTravelTime;
        float speed = Mathf.Min(warpSpeed, remaining / Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(dashDirection * speed, 0f);
    }

    private void EndDash()
    {
        isDashing = false;
        isDashMoving = false;
        rb.gravityScale = defaultGravityScale;
        rb.linearVelocity = Vector2.zero;
        nextDashTime = Time.time + dashCooldown;
        anim.SetBool("Dashing", false);
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
