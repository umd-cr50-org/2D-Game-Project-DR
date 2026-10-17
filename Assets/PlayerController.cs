using System;
using System.Collections.Generic;
using UnityEngine;

// Movement for any character: walking (slope-aware), jumping and double jumping, dashing, air limits, free fall.
// It doesn't read the keyboard itself; it reads a CharacterInput on the same object. The player gets
// KeyboardCharacterInput (added automatically); a boss gets BossAI. Only the keyboard-driven one is the
// player (PlayerController.Instance, followed by the camera).
public class PlayerController : MonoBehaviour
{
    [Header ("Horizontal Movement Settings")]
    private Rigidbody2D rb;
[SerializeField] private float walkspeed = 5;//7
    private float xAxis;
    Animator anim;
    SpriteRenderer sr;
    private CharacterInput input;
    private readonly HashSet<string> animBools = new HashSet<string>(); // bool parameters this character's animator has

    [Header ("Facing")]
    [Tooltip("Tick if this character's sprite is drawn facing right (the player's is drawn facing left).")]
    [SerializeField] private bool spriteFacesRight = false;

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

    // Slopes: while standing on ground, gravity is off and walking follows the surface,
    // so slanted platforms behave like flat ones (no sliding, same speed up and down).
    [Header ("Slope Settings")]
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 50f; // steeper than this counts as a wall
    private const float jumpGroundIgnoreTime = 0.1f; // just after a jump, ground contact doesn't count as standing
    private readonly ContactPoint2D[] groundContacts = new ContactPoint2D[8];
    private Vector2 groundNormal = Vector2.up;
    private float lastJumpTime = -1f;
    private float lastGroundedTime;
    private const float airborneGraceTime = 0.08f; // off the ground this long (without jumping) before counting as in the air
    private bool isGroundStuck; // gravity is off because we're standing on ground
    private bool IsStandingOnGround => isGrounded && Time.time >= lastJumpTime + jumpGroundIgnoreTime;

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

    private PlayerAttack attack;
    private MagicCaster caster;

    public static PlayerController Instance; // the keyboard-controlled player
    public CharacterInput InputSource => input;
    public bool IsPlayer => input is KeyboardCharacterInput;
    public bool IsDashing => isDashing;
    public bool IsGrounded => isGrounded;
    public bool IsFreeFalling => isFreeFalling;
    public bool CanStartAirString => !isFreeFalling && (airStringsUsed == 0 || (airStringsUsed == 1 && dashedSinceAirString));
    private bool CanDashNow => !isFreeFalling && (isGrounded || !airDashUsed);
    public bool CanDash => !isDashing && !IsAttacking && CanDashNow && Time.time >= nextDashTime; // a dash would start now
    public bool IsCasting => caster != null && caster.IsCasting;
    // Casting magic holds the character in place just like an attack does
    private bool IsAttacking => (attack != null && attack.IsAttacking) || IsCasting;
    private bool IsAirAttacking => attack != null && attack.IsAirAttacking;
    private bool isHovering; // gravity is off because an air string is holding the player up
    private bool isInAir; // off the ground for real (see CheckGrounded)
    private float airSince = -1f; // when isInAir last turned on (-1 while on the ground)
    private const float airAnimationDelay = 0.12f; // off the ground this long (without jumping) before the air animation shows
    private int airAnimationHash; // see SetBodyAnimations
    private int groundAnimationHash;
    private int runAnimationHash;
    private PlayerController standingOnCharacter; // the character whose head we're on top of this frame (slide off it)

    [Header ("Characters")]
    [Tooltip("Characters can't stand on each other: anyone landing on a character's head is pushed off sideways at this speed.")]
    [SerializeField] private float slideOffCharacterSpeed = 25f;


    private void Awake()
    {
        input = GetComponent<CharacterInput>();
        if (input == null)
        {
            input = gameObject.AddComponent<KeyboardCharacterInput>();
        }

        // Only the keyboard-driven character is "the player"; AI characters (the boss) don't touch Instance
        if (!IsPlayer)
        {
            return;
        }
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
        caster = GetComponent<MagicCaster>();
        normalColor = sr.color;
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (AnimatorControllerParameter p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool)
                {
                    animBools.Add(p.name);
                }
            }
        }

        // Only contacts whose normal points up (within maxSlopeAngle) count as ground
        groundFilter.useTriggers = false;
        groundFilter.SetNormalAngle(90f - maxSlopeAngle, 90f + maxSlopeAngle);
    }

    // Update is called once per frame
    void Update()
    {
        GetInputs();
        CheckGrounded();
        RefreshAirLimits();
        Dash();
        AirAttackHover();
        UpdateGroundStick();
        if (IsAttacking)
        {
            if (isGrounded)
            {
                // Rooted in place for each jab (fully still when standing, so slopes don't slide)
                isCarryingMomentum = false;
                rb.linearVelocity = isGroundStuck ? Vector2.zero : new Vector2(0f, rb.linearVelocity.y);
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
        SlideOffCharacter();
    }

    // On top of another character: push off to whichever side we're already leaning toward
    private void SlideOffCharacter()
    {
        if (standingOnCharacter == null || isDashing)
        {
            return;
        }
        float dx = transform.position.x - standingOnCharacter.transform.position.x;
        float side = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : -FacingSign();
        rb.linearVelocity = new Vector2(side * slideOffCharacterSpeed, Mathf.Min(rb.linearVelocity.y, 0f));
    }

    // After every script's Update (so an attack that ended this frame is already over), and re-evaluated
    // on the spot, so the right pose is what gets drawn this frame - no one-frame flash of the idle pose
    void LateUpdate()
    {
        UpdateBodyAnimation();
    }

    // Optional: drive the character's basic animations straight from code (no animator links needed):
    //   in the air                  -> airState     (jumps, double jumps, falls, air dashes)
    //   on the ground, moving/dash  -> runState     (walking, running and dashing all use it)
    //   on the ground, still        -> groundState
    // Attacks and spells play their own animations and cut into these. Each state must exist in the
    // character's Animator Controller. Used by the boss (BossAI); the player doesn't call it, so it's unaffected.
    public void SetBodyAnimations(string groundState, string runState, string airState)
    {
        groundAnimationHash = string.IsNullOrEmpty(groundState) ? 0 : Animator.StringToHash(groundState);
        runAnimationHash = string.IsNullOrEmpty(runState) ? 0 : Animator.StringToHash(runState);
        airAnimationHash = string.IsNullOrEmpty(airState) ? 0 : Animator.StringToHash(airState);
    }

    private void UpdateBodyAnimation()
    {
        if (groundAnimationHash == 0 && runAnimationHash == 0 && airAnimationHash == 0)
        {
            return; // not using code-driven animations (the player)
        }
        if (anim == null || anim.runtimeAnimatorController == null || IsAttacking)
        {
            return; // attacks and spells play their own animations
        }

        int current = anim.GetCurrentAnimatorStateInfo(0).shortNameHash;
        bool managed = current == groundAnimationHash || current == runAnimationHash || current == airAnimationHash
            || IsLeftoverAttackPose(current);
        if (!managed)
        {
            return; // some other animation is playing (e.g. a defeat animation): leave it alone
        }

        // Brief losses of ground contact (bumps, slope edges at speed) don't count: only a real jump,
        // or being off the ground for a moment, switches to the air animation
        bool jumpedRecently = Time.time - lastJumpTime < 0.2f;
        bool showAir = isInAir && (jumpedRecently || (airSince >= 0f && Time.time - airSince >= airAnimationDelay));

        int target;
        if (showAir)
        {
            target = airAnimationHash;
        }
        else if (isInAir)
        {
            return; // just left the ground: hold the current pose for a moment
        }
        else
        {
            bool moving = isDashing || Mathf.Abs(xAxis) > 0.01f;
            target = moving ? runAnimationHash : groundAnimationHash;
        }

        if (target == 0 || !anim.HasState(0, target))
        {
            target = groundAnimationHash; // e.g. no running state yet: fall back to the idle pose
        }
        if (target == 0 || target == current || !anim.HasState(0, target))
        {
            return;
        }
        anim.Play(target, 0, 0f);
        anim.Update(0f);
    }

    // An attack or spell animation still showing after it ended (its way back to idle may be missing)
    private bool IsLeftoverAttackPose(int stateHash)
    {
        return (attack != null && attack.IsAttackState(stateHash))
            || (caster != null && caster.IsCastState(stateHash));
    }

    // Copies another character's movement settings (speed, jump, dash, slopes, gravity) onto this one.
    // The boss uses it to move exactly like the player.
    public void MatchMovement(PlayerController other)
    {
        walkspeed = other.walkspeed;
        dashDistance = other.dashDistance;
        dashWindup = other.dashWindup;
        dashTravelTime = other.dashTravelTime;
        dashCooldown = other.dashCooldown;
        dashMomentumDecay = other.dashMomentumDecay;
        jumpForce = other.jumpForce;
        jumpCutMultiplier = other.jumpCutMultiplier;
        maxJumps = other.maxJumps;
        jumpsRemaining = maxJumps;
        maxSlopeAngle = other.maxSlopeAngle;
        groundFilter.SetNormalAngle(90f - maxSlopeAngle, 90f + maxSlopeAngle);
        freeFallDriftMultiplier = other.freeFallDriftMultiplier;

        // Same gravity (the other character's normal gravity, not whatever it's using this frame)
        Rigidbody2D otherRb = other.GetComponent<Rigidbody2D>();
        float gravity = other.defaultGravityScale > 0f ? other.defaultGravityScale : (otherRb != null ? otherRb.gravityScale : 1f);
        Rigidbody2D body = rb != null ? rb : GetComponent<Rigidbody2D>();
        defaultGravityScale = gravity;
        if (body != null && !isDashing && !isHovering && !isGroundStuck)
        {
            body.gravityScale = gravity;
        }
    }

    public float WalkSpeed
    {
        get => walkspeed;
        set => walkspeed = value;
    }
    void GetInputs()
    {
        xAxis = input.Horizontal;
    }

    // Sets an animator bool only if this character's animator has it, so characters with a different
    // (or no) animator don't spam warnings
    public void SetAnimBool(string parameter, bool value)
    {
        if (animBools.Contains(parameter))
        {
            anim.SetBool(parameter, value);
        }
    }

    // 1 when facing right, -1 when facing left (works in the editor too, for hitbox previews)
    public float FacingSign()
    {
        SpriteRenderer s = sr != null ? sr : GetComponentInChildren<SpriteRenderer>();
        bool facingRight = s != null && (s.flipX != spriteFacesRight);
        return facingRight ? 1f : -1f;
    }

    // Turn to face a direction (positive = right); 0 keeps the current facing
    public void Face(float direction)
    {
        if (direction == 0f || sr == null)
        {
            return;
        }
        sr.flipX = (direction > 0f) != spriteFacesRight;
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
           if (isGroundStuck)
           {
               // Walk along the surface: same speed on slopes as on flat ground, and no sliding when still
               Vector2 alongGround = new Vector2(groundNormal.y, -groundNormal.x); // points "right" along the slope
               rb.linearVelocity = alongGround * targetX;
           }
           else
           {
               rb.linearVelocity = new Vector2(targetX, rb.linearVelocity.y);
           }
           SetAnimBool("Walking", rb.linearVelocity.x != 0 && isGrounded);
           Flip();
    }

    // Double-tap left or right to warp that way: hold still for the throw, then shoot forward dashDistance.
    // (An AI can also ask for a dash directly through CharacterInput.DashDirection.)
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

        bool canStartDash = !isDashing && !IsAttacking && CanDashNow && Time.time >= nextDashTime;
        float requestedDirection = 0f;
        if (tapDirection != 0)
        {
            bool isDoubleTap = tapDirection == lastTapDirection
                && Time.time - lastTapTime <= doubleTapWindow;

            if (isDoubleTap && canStartDash)
            {
                requestedDirection = tapDirection;
            }
            else
            {
                lastTapTime = Time.time;
                lastTapDirection = tapDirection;
            }
        }
        if (input.DashDirection != 0 && canStartDash)
        {
            requestedDirection = Mathf.Sign(input.DashDirection);
        }

        if (requestedDirection != 0)
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
            dashDirection = requestedDirection;
            dashMoveStartTime = Time.time + dashWindup;
            rb.gravityScale = 0f; // hang in place for the throw and the warp
            Face(dashDirection);
            SetAnimBool("Dashing", true);
            lastTapTime = -1f; // a third tap shouldn't count as another double tap
        }

        if (!isDashing)
        {
            return;
        }

        // Jumping or pushing the other way breaks out of the dash, keeping whatever speed it had
        bool pushedOtherWay = xAxis != 0 && Mathf.Sign(xAxis) != dashDirection;
        if (input.JumpPressed || pushedOtherWay)
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
        SetAnimBool("Dashing", false);
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
        SetAnimBool("Dashing", false);
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
        if (!IsStandingOnGround)
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
        SetAnimBool("FreeFall", on);
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

    // Standing on ground (any walkable slope) switches gravity off so nothing pulls the player down the slope;
    // Move then keeps them on the surface. Leaving the ground or jumping turns gravity back on.
    // Dash and the air-string hover manage gravity themselves, so this stays out of their way.
    private void UpdateGroundStick()
    {
        bool shouldStick = IsStandingOnGround && !isDashing && !isHovering;
        if (shouldStick && !isGroundStuck)
        {
            isGroundStuck = true;
            rb.gravityScale = 0f;
            // Drop the falling speed but keep the sideways speed, laid along the ground (so dash momentum survives landing)
            Vector2 alongGround = new Vector2(groundNormal.y, -groundNormal.x);
            rb.linearVelocity = alongGround * rb.linearVelocity.x;
        }
        else if (!shouldStick && isGroundStuck)
        {
            ReleaseGroundStick();
        }
    }

    private void ReleaseGroundStick()
    {
        isGroundStuck = false;
        if (!isDashing && !isHovering)
        {
            rb.gravityScale = defaultGravityScale;
        }
    }

    // Face the direction of input; keep the last facing when there's no input
    private void Flip()
    {
        Face(xAxis);
    }

    private void Jump()
    {
        // Refill jumps once we've landed (and aren't still leaving the ground from a jump)
        if (IsStandingOnGround)
        {
            jumpsRemaining = maxJumps;
            isDoubleJumping = false;
        }

        if (input.JumpReleased && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpCutMultiplier);
        }

        bool startedDoubleJump = false;

        if (input.JumpPressed && jumpsRemaining > 0)
        {
            lastJumpTime = Time.time;
            if (isGroundStuck)
            {
                ReleaseGroundStick(); // gravity back on right away for the jump arc
            }
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpsRemaining--;

            // Only the jump after the first one has been used counts as a double jump
            if (jumpsRemaining < maxJumps - 1)
            {
                isDoubleJumping = true;
                startedDoubleJump = true;
            }
        }

        SetAnimBool("Jumping", !isGrounded && !isDoubleJumping);
        SetAnimBool("DoubleJumping", isDoubleJumping);

        // Start the double jump animation from its first frame on the exact frame of the input
        if (startedDoubleJump && anim != null && anim.runtimeAnimatorController != null)
        {
            // State may be named after the clip if the controller wasn't built by PlayerAnimatorSetup.
            // Characters without a double jump state (the boss) replay their jump animation instead.
            bool played = false;
            foreach (string stateName in new[] { "DoubleJump", "Player_Double_Jump" })
            {
                int stateHash = Animator.StringToHash(stateName);
                if (anim.HasState(0, stateHash))
                {
                    anim.Play(stateHash, 0, 0f);
                    played = true;
                    break;
                }
            }
            if (!played && airAnimationHash != 0 && anim.HasState(0, airAnimationHash))
            {
                anim.Play(airAnimationHash, 0, 0f);
                played = true;
            }
            if (played)
            {
                anim.Update(0f);
            }
        }
    }

    // Count as grounded when touching something below us (not walls or ceilings).
    // Checked every frame so leaving a wall doesn't clear grounded while still on the floor.
    // Also records the ground's surface normal (the flattest contact) so movement can follow slopes.
    // Another character's head is never ground: it's remembered in standingOnCharacter so we slide off it.
    private void CheckGrounded()
    {
        int count = rb.GetContacts(groundFilter, groundContacts);
        isGrounded = false;
        standingOnCharacter = null;
        groundNormal = Vector2.up;
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = groundContacts[i];
            Collider2D other = contact.collider.attachedRigidbody == rb ? contact.otherCollider : contact.collider;
            PlayerController character = other != null ? other.GetComponentInParent<PlayerController>() : null;
            if (character != null && character != this)
            {
                standingOnCharacter = character;
                continue;
            }
            if (!isGrounded || contact.normal.y > groundNormal.y)
            {
                groundNormal = contact.normal;
            }
            isGrounded = true;
        }

        // "InAir" is updated every frame whatever the character is doing (dashing, attacking, free fall),
        // so an animator can always show its in-the-air animation. Losing the ground for a moment
        // (walking over a slope's crest) doesn't count; leaving it with a jump counts right away.
        if (isGrounded)
        {
            lastGroundedTime = Time.time;
        }
        bool justJumped = Time.time - lastJumpTime < jumpGroundIgnoreTime;
        isInAir = justJumped || (!isGrounded && Time.time - lastGroundedTime > airborneGraceTime);
        if (!isInAir)
        {
            airSince = -1f;
        }
        else if (airSince < 0f)
        {
            airSince = Time.time;
        }
        SetAnimBool("InAir", isInAir);
    }
}
