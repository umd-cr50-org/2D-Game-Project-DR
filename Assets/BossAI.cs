using UnityEngine;
using UnityEngine.Serialization;

// The boss's "controller": instead of reading the keyboard it decides what to press, and the boss's own
// PlayerController + PlayerAttack carry it out. So the boss has everything the player has - gravity,
// slopes, jumps and double jumps, dashes, the ground jab string, the air string, air limits, free fall.
//
// All distances are measured between the two characters' colliders (the empty space between their bodies),
// and heights between their feet, so the settings mean the same thing however big the sprites are scaled.
//
// Behaviour:
//   Dormant  - stands still until the player comes within battleStartGap; the battle starts right then.
//   Pursue   - picks an action from where the player is, in this order:
//                close range, level with the player   -> ground jab string (minHits..maxHits presses)
//                close range, player above             -> jump (double jump if they're high) into the air string
//                player running away, gap in magic range -> magic (if the MagicCaster is ready)
//                gap in dash range                     -> dash at the player to close the distance
//                farther                               -> walk at them; jump up ledges / when stuck
//   Cooldown - after EVERY attack (strings and magic) the boss does nothing for actionCooldown seconds.
//   Phase 2  - below phase2At health it waits less between attacks and moves faster.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerController), typeof(PlayerAttack), typeof(Rigidbody2D))]
public class BossAI : CharacterInput
{
    private enum State { Dormant, Pursue, JumpAttack, Attacking, Casting, Cooldown }

    [Header ("Movement")]
    [Tooltip("When the game starts, copy the player's gravity, jump, dash and slope settings onto the boss so it moves like the player.")]
    [SerializeField] private bool matchPlayerMovement = true;
    [Tooltip("How fast the boss walks/runs (the player's is 31.7). Can be changed during Play mode.")]
    [SerializeField] private float walkSpeed = 32f;

    [Header ("Animation (each must be a state in the boss's Animator Controller)")]
    [Tooltip("Played the whole time the boss is in the air (jumps, double jumps, falls), unless it's attacking or casting.")]
    [SerializeField] private string airAnimation = "Boss_Jumping";
    [Tooltip("Played on the ground while walking, running or dashing.")]
    [SerializeField] private string runAnimation = "Boss_Running";
    [Tooltip("Played on the ground while standing still.")]
    [SerializeField] private string groundAnimation = "Boss_Idle";

    [Header ("Battle Start")]
    [Tooltip("The battle starts as soon as the gap between the boss and the player is this small (world units).")]
    [SerializeField] private float battleStartGap = 50f;

    [Header ("Pacing")]
    [Tooltip("After every attack the boss waits this long before its next action.")]
    [SerializeField] private float actionCooldown = 2f;
    [Tooltip("Time between the boss's attack presses inside one string.")]
    [SerializeField] private float pressInterval = 0.12f;
    [SerializeField, Range(1, 4)] private int minHits = 2;
    [SerializeField, Range(1, 4)] private int maxHits = 4;

    [Header ("Reading The Player's Position (world units, body to body)")]
    [Tooltip("Ground string when the gap to the player is at most this...")]
    [SerializeField] private float groundAttackGap = 3f;
    [Tooltip("...and their feet are at most this much higher or lower than the boss's.")]
    [SerializeField] private float levelTolerance = 4f;
    [Tooltip("Jump attack when the player is above and the sideways gap is at most this.")]
    [SerializeField] private float airAttackGap = 8f;
    [Tooltip("Use the double jump when the player's feet are at least this much higher.")]
    [SerializeField] private float doubleJumpRise = 9f;
    [Tooltip("Jump if walking has made no progress for this long (a step or wall in the way).")]
    [SerializeField] private float stuckJumpTime = 0.4f;
    [Tooltip("The boss only turns to face the player once their centers are this far apart sideways (stops it flipping back and forth when the player is right above it).")]
    [SerializeField] private float turnDeadzone = 2f;

    [Header ("Dash (world units, body to body)")]
    [Tooltip("The boss dashes at the player when the gap is at least this...")]
    [SerializeField] private float dashMinGap = 6f;
    [Tooltip("...and at most this. Closer than the minimum it walks in; farther than the maximum it walks.")]
    [FormerlySerializedAs("midRangeGap")]
    [SerializeField] private float dashMaxGap = 35f;

    [Header ("Magic (needs a Magic Caster on the boss)")]
    [Tooltip("The boss casts when the gap to the player is at least this...")]
    [SerializeField] private float magicMinGap = 10f;
    [Tooltip("...and at most this.")]
    [SerializeField] private float magicMaxGap = 60f;
    [Tooltip("Only cast while the player is running away. Untick to cast whenever the player is in the magic range.")]
    [SerializeField] private bool magicOnlyWhenRunning = true;
    [Tooltip("The player counts as running when moving away from the boss at least this fast...")]
    [SerializeField] private float runningSpeed = 15f;
    [Tooltip("...for at least this long.")]
    [SerializeField] private float runningTime = 0.4f;

    [Header ("Phase 2 (needs a Health on the boss)")]
    [Tooltip("Phase 2 starts when the boss's health drops to this fraction (0.5 = half).")]
    [SerializeField, Range(0f, 1f)] private float phase2At = 0.5f;
    [Tooltip("In phase 2 the wait after each attack is multiplied by this (0.5 = half as long).")]
    [SerializeField] private float phase2CooldownMultiplier = 0.5f;
    [Tooltip("In phase 2 Walk Speed is multiplied by this.")]
    [SerializeField] private float phase2SpeedMultiplier = 1.25f;

    [SerializeField] private bool logDecisions; // prints what the boss decides to the Console

    public bool InPhase2 { get; private set; }
    public float Phase2At => phase2At;
    public event System.Action EnteredPhase2;
    private float ActionCooldown => actionCooldown * (InPhase2 ? phase2CooldownMultiplier : 1f);

    // This frame's "button presses" (read by PlayerController / PlayerAttack after this script runs)
    private float horizontal;
    private bool jumpPressed;
    private bool attackPressed;
    private int dashDirection;
    public override float Horizontal => horizontal;
    public override bool JumpPressed => jumpPressed;
    public override bool JumpReleased => false; // the boss always does full-height jumps
    public override bool AttackPressed => attackPressed;
    public override int DashDirection => dashDirection;

    public bool BattleStarted => state != State.Dormant;

    private State state = State.Dormant;
    private float stateTime; // when the current state began
    private float timer; // state-specific deadline
    private int pressesLeft;
    private float nextPressTime;
    private bool attackStarted;
    private bool wantsDoubleJump;
    private bool doubleJumpUsed;
    private float stuckSince = -1f;
    private bool magicPressed;
    private bool castStarted;
    private float runningSince = -1f;

    private PlayerController body;
    private PlayerAttack attack;
    private MagicCaster caster;
    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private PlayerController target;
    private Collider2D targetCollider;
    private Rigidbody2D targetRb;
    private Health targetHealth;
    private Health health;

    public override bool MagicPressed => magicPressed;
    private bool PlayerIsRunning => runningSince >= 0f && Time.time - runningSince >= runningTime;

    // Where the player is, measured body to body
    private float direction; // -1 = player is to the left, 1 = to the right
    private float gapX; // empty space between the two bodies, sideways (0 when overlapping)
    private float rise; // how much higher the player's feet are than the boss's (negative = lower)
    private float gap; // overall empty space between the bodies

    void Start()
    {
        body = GetComponent<PlayerController>();
        attack = GetComponent<PlayerAttack>();
        caster = GetComponent<MagicCaster>();
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();

        if (matchPlayerMovement && PlayerController.Instance != null)
        {
            body.MatchMovement(PlayerController.Instance);
        }
        body.WalkSpeed = walkSpeed;

        health = GetComponent<Health>();
        if (health != null)
        {
            health.Changed += OnHealthChanged;
        }
        body.SetBodyAnimations(groundAnimation, runAnimation, airAnimation);
    }

    void Update()
    {
        // Presses only last one frame
        horizontal = 0f;
        jumpPressed = false;
        attackPressed = false;
        dashDirection = 0;
        magicPressed = false;
        body.WalkSpeed = walkSpeed * (InPhase2 ? phase2SpeedMultiplier : 1f); // so Walk Speed can be tuned live in Play mode

        // Defeated or game paused: no actions
        if ((health != null && health.IsDead) || Time.timeScale == 0f)
        {
            return;
        }

        if (!MeasurePlayer())
        {
            return;
        }
        if (targetHealth != null && targetHealth.IsDead) // the player is down: the fight is over
        {
            return;
        }
        TrackPlayerRunning();

        switch (state)
        {
            case State.Dormant:
                if (gap <= battleStartGap)
                {
                    Log("Battle start");
                    SetState(State.Pursue);
                    Pursue();
                }
                break;

            case State.Pursue:
                Pursue();
                break;

            case State.JumpAttack:
                JumpAttack();
                break;

            case State.Attacking:
                Attacking();
                break;

            case State.Casting:
                Casting();
                break;

            case State.Cooldown:
                body.Face(direction); // watches the player, but takes no action
                if (Time.time >= timer)
                {
                    SetState(State.Pursue);
                }
                break;
        }
    }

    // Fills in direction / gapX / rise / gap. Returns false if there's no player to fight.
    private bool MeasurePlayer()
    {
        if (target == null)
        {
            target = PlayerController.Instance;
            targetCollider = target != null ? target.GetComponent<Collider2D>() : null;
            targetRb = target != null ? target.GetComponent<Rigidbody2D>() : null;
            targetHealth = target != null ? target.GetComponent<Health>() : null;
            if (caster != null && target != null)
            {
                caster.AimTarget = target.transform; // spells fly at the player
            }
        }
        if (target == null)
        {
            return false;
        }

        Bounds me = bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.zero);
        Bounds them = targetCollider != null ? targetCollider.bounds : new Bounds(target.transform.position, Vector3.zero);

        float dx = them.center.x - me.center.x;
        // Only turn around once the player is clearly on the other side; when they're nearly straight
        // above/below (or overlapping), keep facing the same way instead of flipping every frame
        if (Mathf.Abs(dx) > turnDeadzone || direction == 0f)
        {
            direction = dx >= 0f ? 1f : -1f;
        }
        gapX = Mathf.Max(0f, Mathf.Abs(dx) - (me.extents.x + them.extents.x));
        rise = them.min.y - me.min.y;
        float gapY = Mathf.Max(0f, Mathf.Abs(them.center.y - me.center.y) - (me.extents.y + them.extents.y));
        gap = Mathf.Sqrt(gapX * gapX + gapY * gapY);
        return true;
    }

    private void Pursue()
    {
        if (body.IsDashing || attack.IsAttacking)
        {
            return;
        }
        if (body.IsFreeFalling)
        {
            horizontal = direction; // helpless: just drift toward the player
            return;
        }

        bool level = Mathf.Abs(rise) <= levelTolerance;
        bool grounded = body.IsGrounded;

        // Level with the player and in reach: jab string
        if (grounded && level && gapX <= groundAttackGap)
        {
            BeginAttack(Random.Range(minHits, maxHits + 1), "ground string");
            return;
        }

        // Already in the air near the player's height: air string straight away
        if (!grounded && level && gapX <= airAttackGap && body.CanStartAirString)
        {
            BeginAttack(Random.Range(minHits, maxHits + 1), "air string");
            return;
        }

        // Player above and close: jump at them, then attack in the air
        if (grounded && rise > levelTolerance && gapX <= airAttackGap)
        {
            jumpPressed = true;
            wantsDoubleJump = rise >= doubleJumpRise;
            doubleJumpUsed = false;
            SetState(State.JumpAttack);
            Log(wantsDoubleJump ? "double jump attack" : "jump attack");
            return;
        }

        // Player in magic range (and running away, unless that's switched off): magic instead of chasing
        bool inMagicRange = gap >= magicMinGap && gap <= magicMaxGap;
        bool magicWanted = inMagicRange && (PlayerIsRunning || !magicOnlyWhenRunning);
        if (magicWanted && caster != null && caster.IsReady && !body.IsFreeFalling)
        {
            body.Face(direction);
            magicPressed = true;
            castStarted = false;
            timer = Time.time + 0.3f; // if no cast has started by then, the press was refused
            SetState(State.Casting);
            Log($"magic (gap {gap:0.0})");
            return;
        }

        // Dash range: dash at the player to close the distance
        if (gap >= dashMinGap && gap <= dashMaxGap && body.CanDash)
        {
            dashDirection = (int)direction;
            Log($"dash in (gap {gap:0.0})");
            return;
        }

        // Otherwise walk at them (stop pushing once touching)
        if (gapX > 0.5f)
        {
            horizontal = direction;
        }

        // Player is up on a higher ledge, or a step / wall is blocking the walk: jump
        bool blocked = grounded && horizontal != 0f && Mathf.Abs(rb.linearVelocity.x) < 0.1f;
        if (blocked)
        {
            if (stuckSince < 0f)
            {
                stuckSince = Time.time;
            }
        }
        else
        {
            stuckSince = -1f;
        }
        bool stuck = stuckSince >= 0f && Time.time - stuckSince >= stuckJumpTime;
        if (grounded && (rise > levelTolerance || stuck))
        {
            jumpPressed = true;
            stuckSince = -1f;
        }
    }

    private void JumpAttack()
    {
        horizontal = gapX > 0.5f ? direction : 0f; // steer toward the player

        // Landed without getting to attack (e.g. bonked a ceiling): rethink
        if (body.IsGrounded && Time.time - stateTime > 0.2f)
        {
            SetState(State.Pursue);
            return;
        }

        // Second jump near the top of the first one
        if (wantsDoubleJump && !doubleJumpUsed && rb.linearVelocity.y < 2f)
        {
            jumpPressed = true;
            doubleJumpUsed = true;
            return;
        }

        // Swing once level with the player, or at the top of the jump(s) if that's as high as it gets
        bool level = Mathf.Abs(rise) <= levelTolerance;
        bool atApex = rb.linearVelocity.y <= 0f && (!wantsDoubleJump || doubleJumpUsed);
        if ((level || atApex) && body.CanStartAirString)
        {
            BeginAttack(Random.Range(minHits, maxHits + 1), "air string");
        }
    }

    private void BeginAttack(int presses, string kind)
    {
        body.Face(direction); // turn to the player before swinging; facing is locked during the string
        pressesLeft = presses;
        nextPressTime = Time.time;
        attackStarted = false;
        timer = Time.time + 0.3f; // if no attack has come out by then, the press was refused
        SetState(State.Attacking);
        Log($"{kind}, {presses} presses");
    }

    private void Attacking()
    {
        if (pressesLeft > 0 && Time.time >= nextPressTime)
        {
            attackPressed = true;
            pressesLeft--;
            nextPressTime = Time.time + pressInterval;
        }

        if (attack.IsAttacking)
        {
            attackStarted = true;
        }
        else if (!attackStarted && Time.time >= timer)
        {
            // The press didn't start an attack (combo cooldown, no air string left...): rethink right away
            SetState(State.Pursue);
            return;
        }

        // String over: every attack is followed by the cooldown
        if (attackStarted && pressesLeft == 0 && !attack.IsAttacking)
        {
            timer = Time.time + ActionCooldown;
            SetState(State.Cooldown);
        }
    }

    private void Casting()
    {
        if (caster.IsCasting)
        {
            castStarted = true;
        }
        else if (!castStarted && Time.time >= timer)
        {
            SetState(State.Pursue); // the cast didn't start (busy / on cooldown): rethink right away
            return;
        }

        // Spell finished: magic is an attack too, so the cooldown follows
        if (castStarted && !caster.IsCasting)
        {
            timer = Time.time + ActionCooldown;
            SetState(State.Cooldown);
        }
    }

    // The player is "running" while moving away from the boss fast; tracks how long they've kept it up
    private void TrackPlayerRunning()
    {
        bool moving = targetRb != null && targetRb.linearVelocity.x * direction >= runningSpeed;
        if (!moving)
        {
            runningSince = -1f;
        }
        else if (runningSince < 0f)
        {
            runningSince = Time.time;
        }
    }

    private void OnHealthChanged(Health h)
    {
        if (!InPhase2 && !h.IsDead && h.Fraction <= phase2At)
        {
            InPhase2 = true;
            Log("Phase 2");
            EnteredPhase2?.Invoke();
        }
    }

    private void SetState(State newState)
    {
        state = newState;
        stateTime = Time.time;
    }

    private void Log(string message)
    {
        if (logDecisions)
        {
            Debug.Log($"{name}: {message}");
        }
    }

    // When the boss is selected: battle start (cyan), dash min/max (magenta), magic min/max (blue),
    // ground attack reach (red), jump attack reach (orange).
    // Each box is the boss's body grown by that gap, so the player's body touching a box means "within that distance".
    void OnDrawGizmosSelected()
    {
        Collider2D c = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        Bounds b = c != null ? c.bounds : new Bounds(transform.position, Vector3.zero);
        Vector3 feet = new Vector3(b.center.x, b.min.y, 0f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(b.center, b.size + new Vector3(battleStartGap * 2f, battleStartGap * 2f, 0f));

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(b.center, b.size + new Vector3(dashMinGap * 2f, dashMinGap * 2f, 0f));
        Gizmos.DrawWireCube(b.center, b.size + new Vector3(dashMaxGap * 2f, dashMaxGap * 2f, 0f));

        Gizmos.color = new Color(0.3f, 0.5f, 1f);
        Gizmos.DrawWireCube(b.center, b.size + new Vector3(magicMinGap * 2f, magicMinGap * 2f, 0f));
        Gizmos.DrawWireCube(b.center, b.size + new Vector3(magicMaxGap * 2f, magicMaxGap * 2f, 0f));

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(b.center, new Vector3(b.size.x + groundAttackGap * 2f, b.size.y + levelTolerance * 2f, 0f));

        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        float top = b.max.y + doubleJumpRise + levelTolerance;
        Vector3 airCenter = new Vector3(b.center.x, (feet.y + levelTolerance + top) * 0.5f, 0f);
        Gizmos.DrawWireCube(airCenter, new Vector3(b.size.x + airAttackGap * 2f, top - (feet.y + levelTolerance), 0f));
    }
}
