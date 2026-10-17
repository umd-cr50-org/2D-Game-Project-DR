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
    }

    [Header ("Input")]
    [SerializeField] private KeyCode attackKey = KeyCode.Z;

    [Header ("Per-Attack Timing")]
    [SerializeField] private AttackTiming[] attacks =
    {
        new AttackTiming { name = "Attack 1" },
        new AttackTiming { name = "Attack 2" },
        new AttackTiming { name = "Attack 3" },
        new AttackTiming { name = "Attack 4 (finisher)" },
    };

    [Header ("Combo")]
    [Tooltip("Which attacks (1-4) a grounded string throws, in order, one per press.")]
    [SerializeField] private int[] groundString = { 1, 2, 3, 4 };
    [Tooltip("Which attacks (1-4) an air string throws, in order, one per press.")]
    [SerializeField] private int[] airString = { 4, 3, 2 };
    [SerializeField] private float comboCooldown = 0.35f; // pause after the last hit of a string before a new one can start
    [SerializeField] private bool logTiming; // prints each hit's length and cancel time to the Console

    private static readonly string[] attackBools = { "Attack1", "Attack2", "Attack3", "Attack4" };
    private static readonly string[] attackStates = { "Player_Attack", "Player_Attack2", "Player_Attack3", "Player_Attack4" }; // animator states, named after their clips
    private static readonly string[] defaultClipNames = attackStates;

    private Animator anim;
    private PlayerController player;
    private int[] currentString = { };
    private int stringIndex = -1; // position in currentString of the hit playing (or last played); -1 = none yet
    private int queuedHits; // presses made during a hit that haven't been thrown yet
    private bool isAttacking;
    private bool isAerial; // currentString is the air string
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
                attacks[i].clip = FindClip(defaultClipNames[i]);
            }
        }
    }

    void Update()
    {
        bool pressed = Input.GetKeyDown(attackKey);

        if (isAttacking)
        {
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

        if (!pressed || Time.time < nextComboTime || player.IsFreeFalling)
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
        hitStartTime = Time.time;
        hitLength = GetHitLength(timing);
        cancelTime = hitLength * timing.cancelPoint;

        SetAttackBool(step);
        if (anim != null)
        {
            anim.speed = timing.playbackSpeed;
            // Jump straight into the hit's state from wherever the animator is (dash, jump, walk...),
            // since the animator only has transitions into the attacks from idle
            int stateHash = Animator.StringToHash(attackStates[step - 1]);
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
        if (anim == null)
        {
            return;
        }
        for (int i = 0; i < attackBools.Length; i++)
        {
            anim.SetBool(attackBools[i], i + 1 == step);
        }
    }
}
