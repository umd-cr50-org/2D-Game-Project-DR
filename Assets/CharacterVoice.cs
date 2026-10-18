using System.Collections;
using UnityEngine;

// Voice lines and the victory pose, for the player and the boss (put one on each).
//   Battle start - when the boss battle begins (the player's line can wait a moment so it answers the boss's).
//   Ultimate     - when this character starts its ultimate.
//   Teleport     - when the boss teleports (boss only).
//   Low health   - when health first drops to Low Health At (again after healing well above it).
//   Defeat       - when this character is defeated.
//   Victory      - when the other side is defeated: once this character is standing on the ground it stops,
//                  plays its Win Animation and says its victory line.
// Each character has its own voice, so a new line cuts off its previous one instead of talking over it.
// Every line is optional: leave a slot empty for silence.
[RequireComponent(typeof(PlayerController))]
public class CharacterVoice : MonoBehaviour
{
    [Header ("Victory")]
    [Tooltip("Animator state played when this character wins (add it to the character's Animator Controller).")]
    [SerializeField] private string winAnimation = "Player_Win";
    [Tooltip("Seconds after the other side is defeated before the win pose and line.")]
    [SerializeField] private float victoryDelay = 0.6f;
    [SerializeField] private SoundEffect victoryLine = new SoundEffect();

    [Header ("Voice Lines")]
    [SerializeField] private SoundEffect battleStartLine = new SoundEffect();
    [Tooltip("Seconds after the battle starts before this character's line (so the two characters take turns).")]
    [SerializeField] private float battleStartDelay = 2f;
    [SerializeField] private SoundEffect ultimateLine = new SoundEffect();
    [Tooltip("Boss only: when it teleports.")]
    [SerializeField] private SoundEffect teleportLine = new SoundEffect();
    [SerializeField] private SoundEffect defeatLine = new SoundEffect();

    [Header ("Low Health")]
    [Tooltip("The low health line and sound play when health first drops to this fraction (0.25 = 25%).")]
    [SerializeField, Range(0f, 1f)] private float lowHealthAt = 0.25f;
    [Tooltip("If healed back above Low Health At + this, the line can play again the next time health drops.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthResetMargin = 0.15f;
    [SerializeField] private SoundEffect lowHealthLine = new SoundEffect();
    [Tooltip("Non-voice audio for low health (e.g. a warning beep or heartbeat), played alongside the line.")]
    [SerializeField] private SoundEffect lowHealthSound = new SoundEffect();

    private PlayerController body;
    private Health health;
    private UltimateAbility ultimate;
    private BossAI ownBoss; // this character's AI, if it's the boss
    private BossAI battleBoss; // the boss whose battle start this character reacts to
    private Health[] opponents = new Health[0];
    private AudioSource voice;
    private bool won;
    private bool lowHealthPlayed;

    // Called by Unity when the component is added in the editor: the boss gets boss settings
    void Reset()
    {
        if (GetComponent<BossAI>() != null)
        {
            winAnimation = "Boss_Win";
            battleStartDelay = 0f; // the boss speaks first
        }
    }

    void Start()
    {
        body = GetComponent<PlayerController>();
        health = GetComponent<Health>();
        ultimate = GetComponent<UltimateAbility>();
        ownBoss = GetComponent<BossAI>();
        battleBoss = ownBoss != null ? ownBoss : FindAnyObjectByType<BossAI>();

        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0f;

        if (health != null)
        {
            health.Died += OnDefeated;
            health.Changed += OnHealthChanged;
        }
        if (ultimate != null)
        {
            ultimate.Started += OnUltimate;
        }
        if (ownBoss != null)
        {
            ownBoss.TeleportStarted += OnTeleport;
        }
        if (battleBoss != null)
        {
            battleBoss.BattleBegan += OnBattleBegan;
        }

        // Everyone else with health is an opponent: when they go down, this character wins
        opponents = System.Array.FindAll(FindObjectsByType<Health>(), h => h != health);
        foreach (Health opponent in opponents)
        {
            opponent.Died += OnOpponentDefeated;
        }
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= OnDefeated;
            health.Changed -= OnHealthChanged;
        }
        if (ultimate != null)
        {
            ultimate.Started -= OnUltimate;
        }
        if (ownBoss != null)
        {
            ownBoss.TeleportStarted -= OnTeleport;
        }
        if (battleBoss != null)
        {
            battleBoss.BattleBegan -= OnBattleBegan;
        }
        foreach (Health opponent in opponents)
        {
            if (opponent != null)
            {
                opponent.Died -= OnOpponentDefeated;
            }
        }
    }

    private void OnHealthChanged(Health h)
    {
        if (h.IsDead)
        {
            return;
        }
        if (!lowHealthPlayed && h.Fraction <= lowHealthAt)
        {
            lowHealthPlayed = true;
            Say(lowHealthLine);
            lowHealthSound.Play();
        }
        else if (lowHealthPlayed && h.Fraction > lowHealthAt + lowHealthResetMargin)
        {
            lowHealthPlayed = false; // healed back up: ready to warn again
        }
    }

    private void OnBattleBegan()
    {
        StartCoroutine(SayAfter(battleStartLine, battleStartDelay));
    }

    private void OnUltimate()
    {
        Say(ultimateLine);
    }

    private void OnTeleport()
    {
        Say(teleportLine);
    }

    private void OnDefeated(Health h)
    {
        StopAllCoroutines(); // no battle line or victory after going down
        Say(defeatLine);
    }

    private void OnOpponentDefeated(Health h)
    {
        if (won || (health != null && health.IsDead))
        {
            return;
        }
        won = true;
        StartCoroutine(Victory());
    }

    private IEnumerator Victory()
    {
        yield return new WaitForSeconds(victoryDelay);

        // Land first (gives up waiting after 2 seconds, e.g. if standing on something odd)
        float giveUpAt = Time.time + 2f;
        while (!body.IsGrounded && Time.time < giveUpAt)
        {
            yield return null;
        }
        if (health != null && health.IsDead)
        {
            yield break;
        }
        body.Celebrate(winAnimation);
        Say(victoryLine);
    }

    private IEnumerator SayAfter(SoundEffect line, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }
        if (health == null || !health.IsDead)
        {
            Say(line);
        }
    }

    // Plays a line on this character's voice, cutting off whatever it was saying
    private void Say(SoundEffect line)
    {
        if (line == null || !line.TryUse()) // no clip, or held back by its Cooldown / Chance / Max Plays
        {
            return;
        }
        voice.Stop();
        voice.clip = line.clip;
        voice.volume = line.volume * SoundPlayer.MasterVolume;
        voice.pitch = 1f; // voices always play at their recorded speed and pitch (Pitch Variation is ignored here)
        voice.Play();
    }
}
