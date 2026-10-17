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
    [SerializeField] private float dashMomentumDecay = 50f; // after a cancelled dash, how fast (units/s per second) the leftover speed bleeds off
    private bool isDashing;
    private bool isCarryingMomentum; // a cancelled dash's speed is still easing back to normal movement
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

    // Per airtime the player gets one air dash and one air string. Air string -> dash -> air string is
    // allowed as a recovery, but after that second string the player is helpless until they land (like Smash).
    [Header ("Air Limits / Free Fall")]
    [SerializeField, Range(0f, 1f)] private float freeFallDriftMultiplier = 0.6f; // sideways control while helpless
    [SerializeField] private Color freeFallTint = new Color(0.55f, 0.55f, 0.55f, 1f); // sprite dims while helpless
    private bool airDashUsed;
    private int airStringsUsed;
    private bool dashedSinceAirString; // dashed after the first air string, which unlocks one more
    private bool isFreeFalling;
    private Color normalColor;
    private bool hasFreeFallParam; // animator has a "FreeFall" bool to drive a helpless animation

    private PlayerAttack attack;

    public static PlayerController Instance;
    public bool IsDashing => isDashing;
    public bool IsGrounded => isGrounded;
    public bool IsFreeFalling => isFreeFalling;
    public bool CanStartAirString => !isFreeFalling && (airStringsUsed == 0 || (airStringsUsed == 1 && dashedSinceAirString));
    private bool CanDashNow => !isFreeFalling && (isGrounded || !airDashUsed);
    private bool IsAttacking => attack != null && attack.IsAttacking;
    private bool IsAirAttacking => attack != null && attack.IsAirAttacking;
    private bool isHovering; // gravity is off because an air string is holding the player up


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
        normalColor = sr.color;
        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.name == "FreeFall" && p.type == AnimatorControllerParameterType.Bool)
            {
                hasFreeFallParam = true;
            }
        }

        // Only contacts whose normal points up (within 45 degrees) count as ground
        groundFilter.useTriggers = false;
        groundFilter.SetNormalAngle(45f, 135f);
    }

    // Update is called once per frame
    void Update()
    {
        GetInputs();
        CheckGrounded();
        RefreshAirLimits();
        Dash();
        AirAttackHover();
        if (IsAttacking)
        {
            if (isGrounded)
            {
                // Rooted in place for each jab; gravity still applies
                isCarryingMomentum = false;
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }
            else if (isHovering)
            {
                // Hang at this height for the air string; sideways speed (walk or dash) eases off to a stop
                float vx = Mathf.MoveTowards(rb.linearVelocity.x, 0f, dashMomentumDecay * Time.deltaTime);
                isCarryingMomentum = false;
                rb.linearVelocity = new Vector2(vx, 0f);
            }
        }
        else if (!isDashing)
        {
            Move();
            if (!isFreeFalling)
            {
                Jump(); // helpless: no jumps until landing
            }
        }
    }
    void GetInputs()
    {
        xAxis = Input.GetAxisRaw("Horizontal");
    }

    private void Move()
    {
           float targetX = xAxis * walkspeed * (isFreeFalling ? freeFallDriftMultiplier : 1f);
           if (isCarryingMomentum)
           {
               // Ease from the cancelled dash's speed into normal walking instead of snapping to it
               float vx = Mathf.MoveTowards(rb.linearVelocity.x, targetX, dashMomentumDecay * Time.deltaTime);
               isCarryingMomentum = vx != targetX;
               targetX = vx;
           }
           rb.linearVelocity = new Vector2(targetX, rb.linearVelocity.y);
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

            if (isDoubleTap && !isDashing && !IsAttacking && CanDashNow && Time.time >= nextDashTime)
            {
                if (!isGrounded)
                {
                    airDashUsed = true;
                    dashedSinceAirString = airStringsUsed > 0;
                }
                if (attack != null)
                {
                    attack.BreakString(); // a dash ends the current string; attacking after it starts a new one
                }
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

        // Jumping or pushing the other way breaks out of the dash, keeping whatever speed it had
        bool pushedOtherWay = xAxis != 0 && Mathf.Sign(xAxis) != dashDirection;
        if (Input.GetButtonDown("Jump") || pushedOtherWay)
        {
            CancelDash();
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

    // Cuts the dash short (animation and all) but leaves the player's current velocity alone,
    // so a dash cancelled mid-warp carries its speed into whatever comes next (Move eases it off).
    // Cancelling during the wind-up just drops the dash, since the player hasn't moved yet.
    public void CancelDash()
    {
        if (!isDashing)
        {
            return;
        }
        isCarryingMomentum = isDashMoving;
        isDashing = false;
        isDashMoving = false;
        rb.gravityScale = defaultGravityScale;
        nextDashTime = Time.time + dashCooldown;
        anim.SetBool("Dashing", false);
    }

    // Called by PlayerAttack when a new air string starts (not for each hit in it)
    public void RegisterAirString()
    {
        airStringsUsed++;
        dashedSinceAirString = false;
    }

    // Called by PlayerAttack when an air string ends: the second one of an airtime leaves the player helpless
    public void AirStringFinished()
    {
        if (airStringsUsed >= 2 && !isGrounded)
        {
            SetFreeFall(true);
        }
    }

    // Landing gives back the air dash and air string, and ends free fall
    private void RefreshAirLimits()
    {
        if (!isGrounded || rb.linearVelocity.y > 0.01f)
        {
            return;
        }
        airDashUsed = false;
        airStringsUsed = 0;
        dashedSinceAirString = false;
        if (isFreeFalling)
        {
            SetFreeFall(false);
        }
    }

    private void SetFreeFall(bool on)
    {
        isFreeFalling = on;
        sr.color = on ? freeFallTint : normalColor;
        if (hasFreeFallParam)
        {
            anim.SetBool("FreeFall", on);
        }
    }

    // While an air string is playing, gravity is switched off so the player stays up until it's finished;
    // it comes back the moment the string ends (or is interrupted).
    private void AirAttackHover()
    {
        if (IsAirAttacking && !isDashing)
        {
            if (!isHovering)
            {
                isHovering = true;
                rb.gravityScale = 0f;
            }
        }
        else if (isHovering)
        {
            isHovering = false;
            rb.gravityScale = defaultGravityScale;
        }
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
