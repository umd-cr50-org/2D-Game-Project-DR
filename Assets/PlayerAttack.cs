using UnityEngine;

// Smash-style jab combo: every press of the attack button throws exactly one hit, up to four in a row.
// Press once -> Attack1 only. Press three times -> Attack1, Attack2, Attack3, one per press.
// A press during a hit is remembered and comes out as the next hit at that hit's cancel point,
// so the combo keeps up with the player's presses. Stop pressing and the combo ends.
// Drives the animator through the bools Attack1 ... Attack4 (only one is true at a time).
// Every hit has its own timing block in the Inspector (see AttackTiming).
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
    [SerializeField] private float comboCooldown = 0.35f; // pause after the finisher before a new combo can start
    [SerializeField] private bool logTiming; // prints each hit's length and cancel time to the Console

    private static readonly string[] attackBools = { "Attack1", "Attack2", "Attack3", "Attack4" };
    private static readonly string[] defaultClipNames = { "Player_Attack", "Player_Attack2", "Player_Attack3", "Player_Attack4" };

    private Animator anim;
    private PlayerController player;
    private int comboStep; // the hit playing (or last played), 1-based; 0 = none yet
    private int queuedHits; // presses made during a hit that haven't been thrown yet
    private bool isAttacking;
    private float hitStartTime;
    private float hitLength;
    private float cancelTime;
    private float nextComboTime;

    public bool IsAttacking => isAttacking;
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
            // Count each press so N presses give exactly N hits, never more than the combo has left
            if (pressed && comboStep + queuedHits < MaxHits)
            {
                queuedHits++;
            }

            float elapsed = Time.time - hitStartTime;
            if (queuedHits > 0 && elapsed >= cancelTime)
            {
                queuedHits--;
                StartHit(comboStep + 1);
                return;
            }

            if (elapsed >= hitLength)
            {
                EndAttack();
            }
            return;
        }

        if (!pressed || player.IsDashing || Time.time < nextComboTime)
        {
            return;
        }

        bool continuesCombo = comboStep > 0 && comboStep < MaxHits
            && Time.time <= hitStartTime + hitLength + attacks[comboStep - 1].comboWindow;
        StartHit(continuesCombo ? comboStep + 1 : 1);
    }

    private void StartHit(int step)
    {
        AttackTiming timing = attacks[step - 1];

        comboStep = step;
        isAttacking = true;
        hitStartTime = Time.time;
        hitLength = GetHitLength(timing);
        cancelTime = hitLength * timing.cancelPoint;

        if (anim != null)
        {
            anim.speed = timing.playbackSpeed;
        }
        SetAttackBool(step);

        if (logTiming)
        {
            Debug.Log($"{timing.name}: length {hitLength:0.00}s, next hit can come out at {cancelTime:0.00}s");
        }
    }

    private void EndAttack()
    {
        isAttacking = false;
        queuedHits = 0;
        if (comboStep >= MaxHits)
        {
            comboStep = 0;
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
