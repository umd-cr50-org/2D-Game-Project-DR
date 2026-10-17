using System.Collections.Generic;
using UnityEngine;

// Smash-style attack strings: every press of the attack button throws exactly one hit.
// On the ground the string is Attack1 -> Attack2 -> Attack3 -> Attack4 (groundString);
// in the air it's Attack4 -> Attack3 -> Attack2 (airString). Press once -> only the first hit,
// press three times -> the first three, one per press.
// A press during a hit is remembered and comes out as the next hit at that hit's cancel point,
// so the combo keeps up with the player's presses. Stop pressing and the string ends.
// Landing mid air string (or leaving the ground mid jab) starts the other string fresh on the next hit.
// Drives the animator through the bools Attack1 ... Attack4 (only one is true at a time).
// Every attack has its own timing block in the Inspector (see AttackTiming), shared by both strings.
[RequireComponent(typeof(PlayerController))]
public class PlayerAttack : MonoBehaviour
{
    [System.Serializable]
    public class AttackTiming
    {
        public string name; // only labels the entry in the Inspector

        [Tooltip("The animation for this hit. Leave empty to find it in the Animator by its default name (Player_Attack, Player_Attack2, ...).")]
        public AnimationClip clip;

        [Tooltip("On: the hit lasts exactly as long as its animation (after Playback Speed). Off: it lasts Duration seconds.")]
        public bool matchClipLength = true;

        [Tooltip("Hit length in seconds, used when Match Clip Length is off or no clip is found.")]
        public float duration = 0.25f;

        [Tooltip("Animation speed for this hit. 2 = twice as fast (half as long). Use it to make a long animation snappier.")]
        [Min(0.05f)] public float playbackSpeed = 1f;

        [Tooltip("How far into this hit (0-1) a press can cut into the next hit. 1 = this animation always plays in full first; 0.5 = the next hit can come out halfway through.")]
        [Range(0f, 1f)] public float cancelPoint = 1f;

        [Tooltip("After this hit finishes, how many seconds a press still continues the combo instead of starting over at hit 1.")]
        public float comboWindow = 0.3f;

        [Tooltip("Where this attack hits. Select the Player and set Preview Attack to drag it around in the Scene view.")]
        public Hitbox hitbox = new Hitbox();
    }

    [System.Serializable]
    public class Hitbox
    {
        [Tooltip("Center of the box from the player's pivot, in the player's local units (the player's collider is 1 x 1). +X is in front of the player and flips with their facing.")]
        public Vector2 offset = new Vector2(0.55f, 0.05f);

        [Tooltip("Width and height of the box, in the player's local units.")]
        public Vector2 size = new Vector2(0.5f, 0.4f);

        [Tooltip("When the box switches on, as a fraction of the hit (0 = first frame, 1 = last frame).")]
        [Range(0f, 1f)] public float activeStart = 0.2f;

        [Tooltip("When the box switches off, as a fraction of the hit.")]
        [Range(0f, 1f)] public float activeEnd = 0.7f;

        public float damage = 1f;

        [Tooltip("Push given to what's hit. X is away from the player (positive = in the direction they face), Y is up.")]
        public Vector2 knockback = new Vector2(3f, 2f);
    }

    [Header ("Per-Attack Timing")]
    [SerializeField] private AttackTiming[] attacks =
    {
        new AttackTiming { name = "Attack 1" },
        new AttackTiming { name = "Attack 2" },
        new AttackTiming { name = "Attack 3", hitbox = new Hitbox { offset = new Vector2(0.6f, 0.05f), size = new Vector2(0.6f, 0.45f) } },
        new AttackTiming { name = "Attack 4 (finisher)", hitbox = new Hitbox { offset = new Vector2(0.65f, 0f), size = new Vector2(0.8f, 0.7f), damage = 2f, knockback = new Vector2(6f, 4f) } },
    };

    [Header ("Hit Detection")]
    [Tooltip("Layers the hitboxes can hit. Put enemies on their own layer and pick it here so the ground isn't checked.")]
    [SerializeField] private LayerMask hittableLayers = ~0;
    [Tooltip("Which attack's hitbox to show and edit in the Scene view (0 = show all, no editing).")]
    [SerializeField, Range(0, 4)] private int previewAttack = 0;
    [SerializeField] private bool logHits; // prints everything a hitbox touches to the Console

    [Header ("Combo")]
    [Tooltip("Which attacks (1-4) a grounded string throws, in order, one per press.")]
    [SerializeField] private int[] groundString = { 1, 2, 3, 4 };
    [Tooltip("Which attacks (1-4) an air string throws, in order, one per press.")]
    [SerializeField] private int[] airString = { 4, 3, 2 };
    [SerializeField] private float comboCooldown = 0.35f; // pause after the last hit of a string before a new one can start
    [SerializeField] private bool logTiming; // prints each hit's length and cancel time to the Console

    [Header ("Animation")]
    [Tooltip("Name of the attack animations/states: \"Player_Attack\" means Player_Attack, Player_Attack2, Player_Attack3, Player_Attack4. Use \"Boss_Attack\" for the boss.")]
    [SerializeField] private string animationPrefix = "Player_Attack";

    private static readonly string[] attackBools = { "Attack1", "Attack2", "Attack3", "Attack4" };

    // The animator state (and clip) for attack 1-4, named after the clips
    private string AttackStateName(int attackNumber)
    {
        return attackNumber == 1 ? animationPrefix : animationPrefix + attackNumber;
    }

    // Whether an animator state (by name hash) is one of this character's attack animations
    public bool IsAttackState(int stateHash)
    {
        for (int n = 1; n <= attackBools.Length; n++)
        {
            if (Animator.StringToHash(AttackStateName(n)) == stateHash)
            {
                return true;
            }
        }
        return false;
    }

    private Animator anim;
    private PlayerController player;
    private int[] currentString = { };
    private int stringIndex = -1; // position in currentString of the hit playing (or last played); -1 = none yet
    private int queuedHits; // presses made during a hit that haven't been thrown yet
    private bool isAttacking;
    private bool isAerial; // currentString is the air string
    private int currentAttack; // 1-4, the attack whose hitbox is live
    private float lastHitProgress; // how far into the hit (0-1) the hitbox was last checked
    private readonly List<Collider2D> overlaps = new List<Collider2D>();
    private readonly HashSet<GameObject> alreadyHit = new HashSet<GameObject>(); // each target is hit once per attack
    private float hitStartTime;
    private float hitLength;
    private float cancelTime;
    private float nextComboTime;

    public bool IsAttacking => isAttacking;
    public bool IsAirAttacking => isAttacking && isAerial;
    private int MaxHits => Mathf.Min(attacks.Length, attackBools.Length);

    void Start()
    {
        anim = GetComponent<Animator>();
        player = GetComponent<PlayerController>();

        // Fill in any clip left empty from the animator, so Match Clip Length works without dragging clips in
        for (int i = 0; i < MaxHits; i++)
        {
            if (attacks[i].clip == null)
            {
                attacks[i].clip = FindClip(AttackStateName(i + 1));
            }
        }
    }

    void Update()
    {
        // The attack button comes from this character's input (keyboard for the player, AI for the boss)
        bool pressed = player.InputSource != null && player.InputSource.AttackPressed;

        if (isAttacking)
        {
            CheckHits();

            // Count each press so N presses give exactly N hits, never more than the string has left
            int hitsLeft = currentString.Length - 1 - stringIndex;
            if (pressed && queuedHits < hitsLeft)
            {
                queuedHits++;
            }

            float elapsed = Time.time - hitStartTime;
            if (queuedHits > 0 && elapsed >= cancelTime)
            {
                queuedHits--;
                bool inAir = !player.IsGrounded;
                if (inAir == isAerial)
                {
                    StartHit(stringIndex + 1);
                }
                else
                {
                    // Landed (or left the ground) mid string: this press opens the other string instead
                    queuedHits = 0;
                    StartString(inAir);
                }
                return;
            }

            if (elapsed >= hitLength)
            {
                EndAttack();
            }
            return;
        }

        if (!pressed || Time.time < nextComboTime || player.IsFreeFalling || player.IsCasting)
        {
            return;
        }

        // A press shortly after a hit ends picks the string back up, as long as we're still on the same
        // footing (ground or air) and the string has hits left; otherwise it starts the right string over
        bool inAirNow = !player.IsGrounded;
        bool continuesString = stringIndex >= 0 && isAerial == inAirNow
            && stringIndex < currentString.Length - 1
            && Time.time <= hitStartTime + hitLength + attacks[AttackNumber(stringIndex) - 1].comboWindow;

        // Out of air strings for this airtime: the press does nothing (and doesn't cancel a dash)
        if (!continuesString && inAirNow && !player.CanStartAirString)
        {
            return;
        }

        // Attacking breaks out of a dash; the dash's momentum carries into the attack
        player.CancelDash();

        if (continuesString)
        {
            StartHit(stringIndex + 1);
        }
        else
        {
            StartString(inAirNow);
        }
    }

    private void StartString(bool inAir)
    {
        if (inAir)
        {
            // Only one air string per airtime (two with a dash in between); PlayerController keeps count
            if (!player.CanStartAirString)
            {
                return;
            }
            player.RegisterAirString();
        }
        isAerial = inAir;
        currentString = inAir ? airString : groundString;
        if (currentString.Length > 0)
        {
            StartHit(0);
        }
    }

    // Which attack (1-4) sits at this position of the current string
    private int AttackNumber(int index)
    {
        return Mathf.Clamp(currentString[index], 1, MaxHits);
    }

    private void StartHit(int index)
    {
        int step = AttackNumber(index);
        AttackTiming timing = attacks[step - 1];

        stringIndex = index;
        isAttacking = true;
        currentAttack = step;
        lastHitProgress = 0f;
        alreadyHit.Clear();
        hitStartTime = Time.time;
        hitLength = GetHitLength(timing);
        cancelTime = hitLength * timing.cancelPoint;

        SetAttackBool(step);
        if (anim != null)
        {
            anim.speed = timing.playbackSpeed;
            // Jump straight into the hit's state from wherever the animator is (dash, jump, walk...),
            // since the animator only has transitions into the attacks from idle
            int stateHash = Animator.StringToHash(AttackStateName(step));
            if (anim.HasState(0, stateHash))
            {
                anim.Play(stateHash, 0, 0f);
            }
        }

        if (logTiming)
        {
            Debug.Log($"{timing.name}: length {hitLength:0.00}s, next hit can come out at {cancelTime:0.00}s");
        }
    }

    // Called by PlayerController when a dash starts: the next press opens a new string instead of continuing this one
    public void BreakString()
    {
        stringIndex = -1;
    }

    private void EndAttack()
    {
        isAttacking = false;
        queuedHits = 0;
        if (isAerial)
        {
            player.AirStringFinished(); // the second air string of an airtime puts the player in free fall
        }
        if (stringIndex >= currentString.Length - 1)
        {
            // Finished the whole string (ground or air): short pause before the next one
            stringIndex = -1;
            nextComboTime = Time.time + comboCooldown;
        }
        if (anim != null)
        {
            anim.speed = 1f;
        }
        // All attack bools off: the animator's "AttackN false" transitions take it back to idle
        SetAttackBool(0);
    }

    // Hits everything in the current attack's box while it's active, each target once per attack.
    // Checks the stretch since last frame, so a short active window can't be skipped by a slow frame.
    private void CheckHits()
    {
        Hitbox hb = attacks[currentAttack - 1].hitbox;
        float progress = hitLength > 0f ? (Time.time - hitStartTime) / hitLength : 1f;
        bool wasActive = progress >= hb.activeStart && lastHitProgress <= hb.activeEnd;
        lastHitProgress = progress;
        if (!wasActive)
        {
            return;
        }

        GetHitboxWorld(currentAttack, out Vector2 center, out Vector2 size);
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(hittableLayers);
        filter.useTriggers = true;
        Physics2D.OverlapBox(center, size, 0f, filter, overlaps);

        foreach (Collider2D col in overlaps)
        {
            if (col.transform.IsChildOf(transform))
            {
                continue; // our own colliders
            }
            GameObject target = col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.gameObject;
            if (!alreadyHit.Add(target))
            {
                continue;
            }

            IHittable hittable = col.GetComponentInParent<IHittable>();
            if (logHits)
            {
                Debug.Log($"Attack{currentAttack} touched {target.name}{(hittable == null ? " (not hittable)" : "")}");
            }
            hittable?.OnHit(new AttackHitInfo
            {
                attackNumber = currentAttack,
                aerial = isAerial,
                damage = hb.damage,
                knockback = new Vector2(hb.knockback.x * FacingSign(), hb.knockback.y),
                attacker = gameObject,
            });
        }
    }

    // 1 when the character faces right, -1 when facing left (PlayerController knows which way its sprite is drawn)
    public float FacingSign()
    {
        PlayerController controller = player != null ? player : GetComponent<PlayerController>();
        return controller != null ? controller.FacingSign() : 1f;
    }

    // An attack's hitbox in world space, flipped to the player's facing and scaled with the player
    public void GetHitboxWorld(int attackNumber, out Vector2 center, out Vector2 size)
    {
        Hitbox hb = attacks[attackNumber - 1].hitbox;
        Vector2 scale = new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        center = (Vector2)transform.position + new Vector2(hb.offset.x * FacingSign() * scale.x, hb.offset.y * scale.y);
        size = Vector2.Scale(hb.size, scale);
    }

    public static readonly Color[] HitboxColors =
    {
        new Color(1f, 0.25f, 0.25f), new Color(1f, 0.6f, 0.1f), new Color(1f, 0.9f, 0.1f), new Color(0.9f, 0.3f, 1f),
    };

    // Draws the hitboxes when the Player is selected; the live one is filled in during play
    void OnDrawGizmosSelected()
    {
        if (attacks == null)
        {
            return;
        }
        for (int i = 0; i < MaxHits; i++)
        {
            if (previewAttack != 0 && previewAttack != i + 1)
            {
                continue;
            }
            GetHitboxWorld(i + 1, out Vector2 center, out Vector2 size);
            Gizmos.color = HitboxColors[i];
            Gizmos.DrawWireCube(center, size);

            Hitbox hb = attacks[i].hitbox;
            float progress = hitLength > 0f ? (Time.time - hitStartTime) / hitLength : 0f;
            if (Application.isPlaying && isAttacking && currentAttack == i + 1
                && progress >= hb.activeStart && progress <= hb.activeEnd)
            {
                Gizmos.color = new Color(HitboxColors[i].r, HitboxColors[i].g, HitboxColors[i].b, 0.35f);
                Gizmos.DrawCube(center, size);
            }
        }
    }

    private float GetHitLength(AttackTiming timing)
    {
        float baseLength = timing.matchClipLength && timing.clip != null ? timing.clip.length : timing.duration;
        return baseLength / timing.playbackSpeed;
    }

    private AnimationClip FindClip(string clipName)
    {
        if (anim == null || anim.runtimeAnimatorController == null)
        {
            return null;
        }
        foreach (AnimationClip c in anim.runtimeAnimatorController.animationClips)
        {
            if (c.name == clipName)
            {
                return c;
            }
        }
        return null;
    }

    // Turns on only the bool for this hit (0 turns them all off)
    private void SetAttackBool(int step)
    {
        for (int i = 0; i < attackBools.Length; i++)
        {
            player.SetAnimBool(attackBools[i], i + 1 == step); // skipped if this character's animator lacks it
        }
    }
}
