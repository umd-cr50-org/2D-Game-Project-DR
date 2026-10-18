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
//                player directly below                 -> magic straight down, or (magic on cooldown) a shockwave
//                                                         that blasts them away with huge knockback
//                player running away, gap in magic range -> magic (if the MagicCaster is ready)
//                gap in dash range                     -> dash at the player to close the distance
//                farther                               -> walk at them; jump up ledges / when stuck
//   Cooldown - after EVERY attack (strings, magic, shockwave) the boss does nothing for actionCooldown seconds.
//   Teleport - when its health first drops past each Teleport At Health mark (75%, 50%, 25%) it fades out
//              and reappears at one of the Teleport Points, away from the player.
//   Phase 2  - below phase2At health the boss fires stronger magic (more orbs, faster), casts whenever the
//              player is in magic range, waits less between attacks and moves faster.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerController), typeof(PlayerAttack), typeof(Rigidbody2D))]
public class BossAI : CharacterInput
{
    private enum State { Dormant, Pursue, JumpAttack, Attacking, Casting, Shockwave, Ultimate, Teleport, Healing, Cooldown }

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
    [Tooltip("Attack animations: \"Boss_Attack\" means Boss_Attack, Boss_Attack2, Boss_Attack3, Boss_Attack4.")]
    [SerializeField] private string attackAnimationPrefix = "Boss_Attack";
    [Tooltip("Played while casting magic.")]
    [SerializeField] private string magicAnimation = "Boss_Magic";
    [Tooltip("Played for the shockwave (when the player is directly below).")]
    [SerializeField] private string shockwaveAnimation = "Boss_Attack3";

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

    [Header ("Player Directly Below (world units, body to body)")]
    [Tooltip("The player counts as directly below when their feet are lower than levelTolerance and the sideways gap is at most this.")]
    [SerializeField] private float belowGap = 4f;
    [Tooltip("Shockwave (used when magic isn't ready): it hits if the gap to the player is at most this when it goes off.")]
    [SerializeField] private float shockwaveReach = 12f;
    [Tooltip("Wind-up before the shockwave goes off.")]
    [SerializeField] private float shockwaveWindup = 0.25f;
    [Tooltip("Time after it goes off before the boss moves on (then the usual cooldown follows).")]
    [SerializeField] private float shockwaveRecovery = 0.3f;
    [SerializeField] private float shockwaveDamage = 3f;
    [Tooltip("X pushes the player away from the boss, Y launches them up. The player's jump force is 40 for scale.")]
    [SerializeField] private Vector2 shockwaveKnockback = new Vector2(55f, 35f);
    [Tooltip("How long the player is helpless after the shockwave (so they really fly).")]
    [SerializeField] private float shockwaveStun = 0.6f;
    [Tooltip("Minimum time between shockwaves.")]
    [SerializeField] private float shockwaveCooldown = 3f;

    [Header ("Phase 2 (needs a Health on the boss)")]
    [Tooltip("Phase 2 starts when the boss's health drops to this fraction (0.5 = half).")]
    [SerializeField, Range(0f, 1f)] private float phase2At = 0.5f;
    [Tooltip("In phase 2 the wait after each attack is multiplied by this (0.5 = half as long).")]
    [SerializeField] private float phase2CooldownMultiplier = 0.5f;
    [Tooltip("In phase 2 Walk Speed is multiplied by this.")]
    [SerializeField] private float phase2SpeedMultiplier = 1.25f;


    [Header ("Phase 2 Magic")]
    [Tooltip("Orbs per cast in phase 2 (fanned out).")]
    [SerializeField, Min(1)] private int phase2ProjectileCount = 3;
    [SerializeField] private float phase2ProjectileSpeedMultiplier = 1.3f;
    [SerializeField] private float phase2ProjectileDamageMultiplier = 1.25f;
    [SerializeField] private Color phase2ProjectileColor = new Color(0.75f, 0.35f, 1f);
    [Tooltip("Optional: a different projectile prefab for phase 2.")]
    [SerializeField] private Projectile phase2ProjectilePrefab;
    [Tooltip("In phase 2, cast whenever the player is in magic range, not only while they run away.")]
    [SerializeField] private bool phase2MagicAnyTime = true;

    [Header ("Teleport (needs Teleport Points)")]
    [Tooltip("Places the boss can teleport to: empty objects in the scene, set on the ground where the boss should land.")]
    [SerializeField] private Transform[] teleportPoints = new Transform[0];
    [Tooltip("The boss teleports (once each) when its health first drops to each of these fractions (0.75 = 75%). " +
             "Add or remove entries for more or fewer teleports.")]
    [SerializeField] private float[] teleportAtHealth = { 0.75f, 0.5f, 0.25f };
    [Tooltip("The boss won't land a point closer to the player than this (body to body), so it never appears on top of them.")]
    [SerializeField] private float teleportMinGapToPlayer = 6f;
    [Tooltip("Seconds to fade out, and again to fade back in. The boss can't be hurt while teleporting.")]
    [SerializeField] private float teleportFadeTime = 0.35f;
    [Tooltip("Optional animator states played while vanishing / reappearing.")]
    [SerializeField] private string teleportOutAnimation = "";
    [SerializeField] private string teleportInAnimation = "";
    [SerializeField] private SoundEffect teleportOutSound = new SoundEffect();
    [SerializeField] private SoundEffect teleportInSound = new SoundEffect();

    [Header ("Healing (teleport heal + heal ability, both play Heal Animation)")]
    [Tooltip("Animator state for both heals.")]
    [SerializeField] private string healAnimation = "Boss_Magic";
    [SerializeField] private Color healGlowColor = new Color(0.3f, 1f, 0.45f, 0.55f);
    [SerializeField] private SoundEffect healSound = new SoundEffect();
    [Tooltip("Teleport heal: health recovered after each health teleport, as a fraction of max health (0.05 = 5%). 0 = off.")]
    [SerializeField, Range(0f, 1f)] private float teleportHealAmount = 0.05f;
    [Tooltip("How long the teleport heal takes (the boss stands casting; hits don't stop this one).")]
    [SerializeField] private float teleportHealTime = 0.8f;
    [Tooltip("Heal ability: health recovered if the channel finishes, as a fraction of max health. 0 = off.")]
    [SerializeField, Range(0f, 1f)] private float healAbilityAmount = 0.15f;
    [Tooltip("How long the heal ability channels. Health comes back gradually; any hit stops it.")]
    [SerializeField] private float healChannelTime = 2.5f;
    [Tooltip("Only uses the heal ability at or below this much health (0.6 = 60%).")]
    [SerializeField, Range(0f, 1f)] private float healBelowHealth = 0.6f;
    [Tooltip("...and only when the player is at least this far away (body to body), so it has time.")]
    [SerializeField] private float healMinGapToPlayer = 20f;
    [Tooltip("Minimum seconds between heal ability uses.")]
    [SerializeField] private float healCooldown = 15f;

    [Header ("Ultimate (needs a Special Meter and an Ultimate Ability on the boss)")]
    [Tooltip("When its gauge is full, the boss uses its ultimate once the gap to the player is at most this (keep it smaller than the Ultimate Ability radius so it reaches).")]
    [SerializeField] private float ultimateUseGap = 12f;

    [Header ("Sounds")]
    [Tooltip("Plays when the battle starts (e.g. a roar).")]
    [SerializeField] private SoundEffect battleStartSound = new SoundEffect();
    [Tooltip("Plays when phase 2 starts.")]
    [SerializeField] private SoundEffect phase2Sound = new SoundEffect();
    [SerializeField] private SoundEffect shockwaveSound = new SoundEffect();

    [SerializeField] private bool logDecisions; // prints what the boss decides to the Console

    public bool InPhase2 { get; private set; }
    public float Phase2At => phase2At;
    public event System.Action EnteredPhase2;
    public event System.Action BattleBegan; // the battle just started (CharacterVoice plays battle lines)
    public event System.Action TeleportStarted; // the boss just began a teleport
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
    private bool ultimatePressed;
    private bool ultimateStarted;
    private UltimateAbility ultimate;
    private bool castStarted;
    private float runningSince = -1f;
    private float nextShockwaveTime;
    private bool[] teleportUsed = new bool[0]; // which Teleport At Health thresholds have fired
    private bool teleportPending; // a threshold was just crossed: teleport as soon as possible
    private float healEndTime;
    private float healPerSecond;
    private bool healInterruptible;
    private float healLastHealth;
    private float nextHealTime;
    private SpriteRenderer healGlow;
    private Vector2 teleportTarget;
    private bool teleportArrived;
    private SpriteRenderer sprite;
    private bool shockwaveFired;
    private Animator anim;

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
    public override bool UltimatePressed => ultimatePressed;
    private bool PlayerIsRunning => runningSince >= 0f && Time.time - runningSince >= runningTime;

    // Where the player is, measured body to body
    private float direction; // -1 = player is to the left, 1 = to the right
    private float gapX; // empty space between the two bodies, sideways (0 when overlapping)
    private float rise; // how much higher the player's feet are than the boss's (negative = lower)
    private float gap; // overall empty space between the bodies

    void Awake()
    {
        // The boss always has magic, even if no Magic Caster was added to it in the scene
        if (GetComponent<MagicCaster>() == null)
        {
            gameObject.AddComponent<MagicCaster>();
        }
    }

    void Start()
    {
        anim = GetComponent<Animator>();
        sprite = GetComponentInChildren<SpriteRenderer>();
        teleportUsed = new bool[teleportAtHealth.Length];
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
        // The boss's own animations, whatever its Player Attack / Magic Caster were set to in the scene
        body.SetBodyAnimations(groundAnimation, runAnimation, airAnimation);
        attack.SetAnimationPrefix(attackAnimationPrefix, isBase: true);
        caster.SetCastState(magicAnimation, isBase: true);
        caster.MeterCost = 0f; // the boss's magic is free; its gauge is only for the ultimate
        ultimate = GetComponent<UltimateAbility>();
    }

    void Update()
    {
        // Presses only last one frame
        horizontal = 0f;
        jumpPressed = false;
        attackPressed = false;
        dashDirection = 0;
        magicPressed = false;
        ultimatePressed = false;
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
        if (targetHealth != null && targetHealth.IsDead && state != State.Teleport) // the player is down: the fight is over (a teleport finishes first)
        {
            return;
        }
        TrackPlayerRunning();

        // Health dropped past a Teleport At Health mark: drop whatever it's doing and teleport away
        if (teleportPending && CanTeleport())
        {
            teleportPending = false;
            if (TryPickPoint(nearPlayer: false, out Vector2 point))
            {
                BeginTeleport(point, $"health {health.Fraction:P0}");
                return;
            }
        }

        switch (state)
        {
            case State.Dormant:
                if (gap <= battleStartGap)
                {
                    Log("Battle start");
                    battleStartSound.Play();
                    BattleBegan?.Invoke();
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

            case State.Ultimate:
                UltimateState();
                break;

            case State.Shockwave:
                Shockwave();
                break;

            case State.Healing:
                HealingState();
                break;

            case State.Teleport:
                TeleportState();
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

        // Hurt and the player keeping their distance: channel a heal
        if (health != null && healAbilityAmount > 0f && Time.time >= nextHealTime && body.IsGrounded
            && health.Fraction <= healBelowHealth && gap >= healMinGapToPlayer)
        {
            nextHealTime = Time.time + healCooldown;
            BeginHeal(healAbilityAmount, healChannelTime, interruptible: true, "heal ability");
            return;
        }

        // Gauge full and the player close enough: ultimate before anything else
        if (ultimate != null && ultimate.IsReady && gap <= ultimateUseGap)
        {
            body.Face(direction);
            ultimatePressed = true;
            ultimateStarted = false;
            timer = Time.time + 0.3f; // if it hasn't started by then, the press was refused
            SetState(State.Ultimate);
            Log($"ULTIMATE (gap {gap:0.0})");
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

        // Player right underneath (none of the moves above can reach them): magic straight down,
        // or if that's on cooldown, a shockwave that blasts them out from under the boss
        if (rise < -levelTolerance && gapX <= belowGap)
        {
            if (caster.IsReady)
            {
                BeginCast("magic down at the player below");
                return;
            }
            if (Time.time >= nextShockwaveTime)
            {
                BeginShockwave();
                return;
            }
        }

        // Player in magic range (and running away, unless that's switched off): magic instead of chasing
        bool inMagicRange = gap >= magicMinGap && gap <= magicMaxGap;
        bool anyTime = !magicOnlyWhenRunning || (InPhase2 && phase2MagicAnyTime);
        bool magicWanted = inMagicRange && (PlayerIsRunning || anyTime);
        if (magicWanted && caster.IsReady)
        {
            BeginCast($"magic (gap {gap:0.0})");
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

    // ---------- Teleport ----------

    private bool CanTeleport()
    {
        // Not in the middle of a teleport or the ultimate (it waits for those to finish)
        return teleportPoints.Length > 0 && state != State.Teleport && state != State.Ultimate
            && body.CanAct && (ultimate == null || !ultimate.IsActive);
    }

    // Picks a teleport point: the one nearest the player (but not on top of them), or a random one away
    // from where the boss is now. Returns the point's position.
    private bool TryPickPoint(bool nearPlayer, out Vector2 point)
    {
        point = Vector2.zero;
        Bounds me = bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.zero);
        Bounds them = targetCollider != null ? targetCollider.bounds : new Bounds(target.transform.position, Vector3.zero);
        float bestScore = float.MaxValue;
        bool found = false;

        foreach (Transform p in teleportPoints)
        {
            if (p == null)
            {
                continue;
            }
            Vector2 pos = p.position;
            // Gap between the player's body and where the boss's body would be standing at this point
            float gapToPlayer = Mathf.Max(0f, Mathf.Abs(pos.x - them.center.x) - (me.extents.x + them.extents.x));
            if (gapToPlayer < teleportMinGapToPlayer && Mathf.Abs(pos.y - them.min.y) < me.size.y)
            {
                continue; // would land on (or right beside) the player
            }
            if (Vector2.Distance(pos, new Vector2(me.center.x, me.min.y)) < me.size.x)
            {
                continue; // that's where the boss already is
            }

            float score;
            if (nearPlayer)
            {
                score = Vector2.Distance(pos, them.center);
            }
            else
            {
                score = Random.value; // random pick among the valid points
            }
            if (score < bestScore)
            {
                bestScore = score;
                point = pos;
                found = true;
            }
        }
        return found;
    }

    private void BeginTeleport(Vector2 point, string reason)
    {
        teleportTarget = point;
        teleportArrived = false;
        timer = Time.time + teleportFadeTime;
        body.Interrupt();
        if (health != null)
        {
            health.Invulnerable = true;
        }
        if (!string.IsNullOrEmpty(teleportOutAnimation))
        {
            body.HoldBodyAnimation = true;
            PlayAnimation(teleportOutAnimation, null);
        }
        UpdateHealGlow(false); // a teleport cuts a heal short
        teleportOutSound.Play();
        TeleportStarted?.Invoke();
        SetState(State.Teleport);
        Log("teleport: " + reason);
    }

    private void TeleportState()
    {
        rb.linearVelocity = Vector2.zero;
        float t = Mathf.Clamp01((timer - Time.time) / Mathf.Max(teleportFadeTime, 0.01f)); // 1 -> 0 over each half

        if (!teleportArrived)
        {
            SetAlpha(t); // fading out
            if (Time.time < timer)
            {
                return;
            }
            // Move so the boss's feet (bottom of its collider) land on the point
            Bounds b = bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.zero);
            Vector2 feetOffset = (Vector2)transform.position - new Vector2(b.center.x, b.min.y);
            Vector2 destination = teleportTarget + feetOffset;
            rb.position = destination;
            transform.position = destination;
            body.Face(direction);
            teleportArrived = true;
            timer = Time.time + teleportFadeTime;
            body.HoldBodyAnimation = false;
            if (!string.IsNullOrEmpty(teleportInAnimation))
            {
                body.HoldBodyAnimation = true;
                PlayAnimation(teleportInAnimation, null);
            }
            teleportInSound.Play();
            return;
        }

        SetAlpha(1f - t); // fading in
        if (Time.time >= timer)
        {
            SetAlpha(1f);
            if (health != null)
            {
                health.Invulnerable = false;
            }
            if (teleportHealAmount > 0f && health != null && !health.IsDead)
            {
                BeginHeal(teleportHealAmount, teleportHealTime, interruptible: false, "teleport heal");
                return;
            }
            if (body.HoldBodyAnimation)
            {
                ReturnToBodyAnimation();
            }
            SetState(State.Pursue);
        }
    }

    // ---------- Healing ----------

    // Stands still casting (Heal Animation, green glow) while health comes back gradually over the duration.
    // Interruptible heals stop the moment the boss takes damage.
    private void BeginHeal(float fractionOfMax, float duration, bool interruptible, string reason)
    {
        duration = Mathf.Max(duration, 0.05f);
        healPerSecond = fractionOfMax * health.Max / duration;
        healEndTime = Time.time + duration;
        healInterruptible = interruptible;
        healLastHealth = health.Current;
        body.Interrupt();
        body.HoldBodyAnimation = true;
        PlayAnimation(healAnimation, null);
        healSound.Play();
        SetState(State.Healing);
        Log($"{reason} (+{fractionOfMax * health.Max:0.#} health over {duration:0.0}s)");
    }

    private void HealingState()
    {
        if (Time.time >= healEndTime)
        {
            EndHeal();
            timer = Time.time + ActionCooldown * 0.5f; // a short breather after healing
            SetState(State.Cooldown);
            return;
        }
        health.Heal(healPerSecond * Time.deltaTime, playSound: false);
        healLastHealth = health.Current;
        UpdateHealGlow(true);
    }

    // Called from OnHealthChanged: damage during an interruptible heal cancels it
    private void CheckHealInterrupted(Health h)
    {
        if (state != State.Healing)
        {
            return;
        }
        if (healInterruptible && h.Current < healLastHealth)
        {
            Log("heal interrupted");
            EndHeal();
            timer = Time.time + ActionCooldown;
            SetState(State.Cooldown);
            return;
        }
        healLastHealth = h.Current;
    }

    private void EndHeal()
    {
        UpdateHealGlow(false);
        ReturnToBodyAnimation();
    }

    private void UpdateHealGlow(bool on)
    {
        if (!on)
        {
            if (healGlow != null)
            {
                healGlow.enabled = false;
            }
            return;
        }
        if (healGlow == null)
        {
            GameObject go = new GameObject("Heal Glow");
            go.transform.SetParent(transform, false);
            healGlow = go.AddComponent<SpriteRenderer>();
            healGlow.sprite = MagicCaster.GetOrbSprite();
            if (sprite != null)
            {
                healGlow.sortingLayerID = sprite.sortingLayerID;
                healGlow.sortingOrder = sprite.sortingOrder - 1;
            }
        }
        healGlow.enabled = true;
        healGlow.color = healGlowColor;
        Bounds b = bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.one);
        float worldSize = Mathf.Max(b.size.x, b.size.y) * 1.6f * (1f + 0.08f * Mathf.Sin(Time.time * 10f));
        Vector3 s = transform.lossyScale;
        healGlow.transform.position = b.center;
        healGlow.transform.localScale = new Vector3(worldSize / Mathf.Abs(s.x), worldSize / Mathf.Abs(s.y), 1f);
    }

    private void SetAlpha(float alpha)
    {
        if (sprite == null)
        {
            return;
        }
        Color c = body.BaseColor;
        c.a *= alpha;
        sprite.color = c;
    }

    private void UltimateState()
    {
        if (ultimate.IsActive)
        {
            ultimateStarted = true;
        }
        else if (!ultimateStarted && Time.time >= timer)
        {
            SetState(State.Pursue); // it didn't start (busy): rethink
            return;
        }
        if (ultimateStarted && !ultimate.IsActive)
        {
            timer = Time.time + ActionCooldown;
            SetState(State.Cooldown);
        }
    }

    private void BeginCast(string reason)
    {
        body.Face(direction);
        magicPressed = true;
        castStarted = false;
        timer = Time.time + 0.3f; // if no cast has started by then, the press was refused
        SetState(State.Casting);
        Log(reason);
    }

    // ---------- Shockwave (player directly below) ----------

    private void BeginShockwave()
    {
        shockwaveFired = false;
        timer = Time.time + shockwaveWindup;
        nextShockwaveTime = Time.time + shockwaveCooldown;
        body.HoldBodyAnimation = true;
        PlayAnimation(shockwaveAnimation, null);
        SetState(State.Shockwave);
        Log("shockwave");
    }

    private void Shockwave()
    {
        if (!shockwaveFired)
        {
            if (Time.time < timer)
            {
                return;
            }
            shockwaveFired = true;
            shockwaveSound.Play();
            timer = Time.time + shockwaveRecovery;
            if (gap <= shockwaveReach)
            {
                // Away from the boss sideways (whichever side the player's center is on) and up
                float dx = target.transform.position.x - transform.position.x;
                float side = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : direction;
                IHittable hittable = target.GetComponent<IHittable>();
                hittable?.OnHit(new AttackHitInfo
                {
                    attackNumber = 0,
                    damage = shockwaveDamage,
                    knockback = new Vector2(side * shockwaveKnockback.x, shockwaveKnockback.y),
                    attacker = gameObject,
                    hitStun = shockwaveStun,
                });
            }
            return;
        }
        if (Time.time >= timer)
        {
            ReturnToBodyAnimation();
            timer = Time.time + ActionCooldown;
            SetState(State.Cooldown);
        }
    }

    // ---------- Phase 2 ----------

    private void OnHealthChanged(Health h)
    {
        if (h.IsDead)
        {
            UpdateHealGlow(false);
        }
        CheckHealInterrupted(h);

        // Health teleports: each threshold fires once (a big hit past several only teleports once)
        for (int i = 0; i < Mathf.Min(teleportAtHealth.Length, teleportUsed.Length); i++)
        {
            if (!teleportUsed[i] && !h.IsDead && h.Fraction <= teleportAtHealth[i])
            {
                teleportUsed[i] = true;
                teleportPending = true;
            }
        }

        if (InPhase2 || h.IsDead || h.Fraction > phase2At)
        {
            return;
        }
        InPhase2 = true;
        Log("Phase 2");
        EnteredPhase2?.Invoke();
        phase2Sound.Play();

        // Stronger magic
        caster.Count = phase2ProjectileCount;
        caster.Speed *= phase2ProjectileSpeedMultiplier;
        caster.Damage *= phase2ProjectileDamageMultiplier;
        caster.OrbColor = phase2ProjectileColor;
        if (phase2ProjectilePrefab != null)
        {
            caster.ProjectilePrefab = phase2ProjectilePrefab;
        }
    }

    // ---------- Animation helpers ----------

    // Plays a state if the animator has it (otherwise the fallback, if that exists)
    private void PlayAnimation(string stateName, string fallback)
    {
        if (anim == null || anim.runtimeAnimatorController == null)
        {
            return;
        }
        string chosen = Pick(stateName, fallback);
        if (!string.IsNullOrEmpty(chosen) && anim.HasState(0, Animator.StringToHash(chosen)))
        {
            anim.Play(chosen, 0, 0f);
            anim.Update(0f);
        }
    }

    // The first name if the animator has that state, otherwise the second
    private string Pick(string preferred, string fallback)
    {
        bool exists = anim != null && anim.runtimeAnimatorController != null && !string.IsNullOrEmpty(preferred)
            && anim.HasState(0, Animator.StringToHash(preferred));
        return exists ? preferred : fallback;
    }

    // After a one-off animation (transformation, shockwave), back to standing; PlayerController takes it from there
    private void ReturnToBodyAnimation()
    {
        body.HoldBodyAnimation = false;
        PlayAnimation(groundAnimation, null);
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

        // Teleport points (purple), and a line to each
        Gizmos.color = new Color(0.7f, 0.3f, 1f);
        foreach (Transform p in teleportPoints)
        {
            if (p != null)
            {
                Gizmos.DrawWireSphere(p.position, 1.5f);
                Gizmos.DrawLine(feet, p.position);
            }
        }
    }
}
