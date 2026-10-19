using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Every sound in the game, in one place. Put one on the Player and one on the Boss for their own sounds and
// voice lines, and fill the shared sections (Music, Menu & Game Sounds) on just ONE of them, so they don't
// play twice. It also works on a plain object, e.g. in the MainMenu scene for the menu music and clicks.
//
//   Music              - fades between tracks as the fight goes on: Starting -> Boss (battle starts) ->
//                        Phase 2 (boss at half health) -> Victory or Defeat (played once).
//   Menu & Game Sounds - button clicks, pausing, the boss bar appearing, health orbs dropping / picked up.
//   Victory            - when the other side is defeated: once standing on the ground this character stops,
//                        plays its Win Animation and says its victory line.
//   Voice Lines        - battle start, ultimate, teleport / phase 2 / shockwave (boss), hurt, heal, defeat, low health.
//   Sound Effects      - jumps, attack swings and hits, magic, gauge charging, ultimate blast.
// Each character has its own voice: a new line cuts off its previous one (hurt / heal grunts never cut anything off).
// Every slot is optional: leave it empty for silence.
public class GameAudio : MonoBehaviour
{
    public enum UISound { ButtonClick, Pause, BossBar }

    [Header ("Music (fill on ONE object only)")]
    [Tooltip("Plays as soon as the scene starts (menu music / area music before the boss).")]
    [SerializeField] private AudioClip startingMusic;
    [SerializeField] private AudioClip bossMusic;
    [Tooltip("Optional: replaces the boss music in phase 2.")]
    [SerializeField] private AudioClip phase2Music;
    [Tooltip("Plays once (no loop).")]
    [SerializeField] private AudioClip victoryMusic;
    [Tooltip("Plays once (no loop).")]
    [SerializeField] private AudioClip defeatMusic;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;
    [Tooltip("Music volume is multiplied by this while the game is paused.")]
    [SerializeField, Range(0f, 1f)] private float pausedMusicVolume = 0.4f;
    [Tooltip("Seconds to fade from one track to the next.")]
    [SerializeField] private float musicFadeTime = 1.5f;

    [Header ("Menu & Game Sounds (fill on ONE object only)")]
    [SerializeField] private SoundEffect buttonClickSound = new SoundEffect();
    [SerializeField] private SoundEffect pauseSound = new SoundEffect();
    [Tooltip("When the boss health bar appears (the battle starts).")]
    [SerializeField] private SoundEffect bossBarSound = new SoundEffect();
    [Tooltip("When the boss drops a health orb.")]
    [SerializeField] private SoundEffect orbDropSound = new SoundEffect();
    [Tooltip("When the player picks a health orb up.")]
    [SerializeField] private SoundEffect orbPickupSound = new SoundEffect();

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
    [Tooltip("Boss only: when phase 2 starts.")]
    [SerializeField] private SoundEffect phase2Line = new SoundEffect();
    [Tooltip("Boss only: when it uses the shockwave (player directly below).")]
    [SerializeField] private SoundEffect shockwaveLine = new SoundEffect();
    [Tooltip("When hit (a grunt). Never cuts off another line. Give it a Cooldown / Chance so it isn't on every hit.")]
    [SerializeField] private SoundEffect hurtLine = new SoundEffect();
    [Tooltip("When healed: the player picking up a health orb, the boss starting a heal. Never cuts off another line.")]
    [SerializeField] private SoundEffect healLine = new SoundEffect();
    [SerializeField] private SoundEffect defeatLine = new SoundEffect();
    [Tooltip("A line that has to wait for a more important one (e.g. the teleport line during the phase 2 line) is dropped if it would start later than this many seconds.")]
    [SerializeField] private float queuedLineMaxWait = 4f;

    [Header ("Low Health")]
    [Tooltip("The low health line and sound play when health first drops to this fraction (0.25 = 25%).")]
    [SerializeField, Range(0f, 1f)] private float lowHealthAt = 0.25f;
    [Tooltip("If healed back above Low Health At + this, the line can play again the next time health drops.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthResetMargin = 0.15f;
    [SerializeField] private SoundEffect lowHealthLine = new SoundEffect();
    [Tooltip("Non-voice audio for low health (e.g. a warning beep or heartbeat), played alongside the line.")]
    [SerializeField] private SoundEffect lowHealthSound = new SoundEffect();
    [Tooltip("On: the Low Health Sound loops (e.g. a heartbeat) for as long as health stays at or below Low Health At. Off: it plays once.")]
    [SerializeField] private bool loopLowHealthSound = true;

    [Header ("Sound Effects")]
    [SerializeField] private SoundEffect jumpSound = new SoundEffect();
    [SerializeField] private SoundEffect doubleJumpSound = new SoundEffect();
    [Tooltip("Swing sound for Attack 1, 2, 3, 4 (in that order).")]
    [SerializeField] private SoundEffect[] attackSwingSounds = { new SoundEffect(), new SoundEffect(), new SoundEffect(), new SoundEffect() };
    [Tooltip("Hit sound for Attack 1, 2, 3, 4 when it lands on something.")]
    [SerializeField] private SoundEffect[] attackHitSounds = { new SoundEffect(), new SoundEffect(), new SoundEffect(), new SoundEffect() };
    [Tooltip("Magic wind-up begins.")]
    [SerializeField] private SoundEffect magicCastSound = new SoundEffect();
    [Tooltip("The spell comes out.")]
    [SerializeField] private SoundEffect magicFireSound = new SoundEffect();
    [Tooltip("A projectile hits something.")]
    [SerializeField] private SoundEffect magicImpactSound = new SoundEffect();
    [SerializeField] private SoundEffect chargeStartSound = new SoundEffect();
    [Tooltip("Loops while charging the gauge.")]
    [SerializeField] private SoundEffect chargeLoopSound = new SoundEffect();
    [Tooltip("The magic gauge just became full.")]
    [SerializeField] private SoundEffect gaugeFullSound = new SoundEffect();
    [Tooltip("The ultimate's blast goes off.")]
    [SerializeField] private SoundEffect ultimateBlastSound = new SoundEffect();

    private static readonly List<GameAudio> all = new List<GameAudio>();

    private PlayerController body;
    private Health health;
    private PlayerAttack attack;
    private MagicCaster caster;
    private SpecialMeter meter;
    private UltimateAbility ultimate;
    private BossAI ownBoss; // this character's AI, if it's the boss
    private BossAI battleBoss; // the boss whose battle (start, phase 2, defeat) drives the music and battle lines
    private Health bossHealth;
    private Health playerHealth;
    private Health[] opponents = new Health[0];
    private AudioSource voice;
    private AudioSource loop;
    private AudioSource lowHealthLoop;
    private AudioSource musicCurrent, musicPrevious;
    private AudioClip wantedMusic;
    private float musicFade = 1f;
    private bool won;
    private bool lowHealthPlayed;
    private float lastHealth;

    // Voice line priorities (see Say)
    private const int GruntPriority = 0, NormalPriority = 1, PhasePriority = 2, FinalPriority = 3;
    private int currentPriority;
    private AudioClip queuedClip;
    private float queuedVolume;
    private int queuedPriority;
    private float queuedUntil;

    // Plays a menu sound (button click, pause, boss bar) from whichever Game Audio has that slot filled
    public static void Play(UISound sound)
    {
        foreach (GameAudio a in all)
        {
            switch (sound)
            {
                case UISound.ButtonClick: a.buttonClickSound.Play(); break;
                case UISound.Pause: a.pauseSound.Play(); break;
                case UISound.BossBar: a.bossBarSound.Play(); break;
            }
        }
    }

    // Called by Unity when the component is added in the editor: the boss gets boss settings
    void Reset()
    {
        if (GetComponent<BossAI>() != null)
        {
            winAnimation = "Boss_Win";
            battleStartDelay = 0f; // the boss speaks first
        }
    }

    void OnEnable()
    {
        all.Add(this);
        HealthDropper.OrbDropped += OnOrbDropped;
        HealthPickup.PickedUp += OnOrbPickedUp;
    }

    void OnDisable()
    {
        all.Remove(this);
        HealthDropper.OrbDropped -= OnOrbDropped;
        HealthPickup.PickedUp -= OnOrbPickedUp;
    }

    void Start()
    {
        body = GetComponent<PlayerController>();
        health = GetComponent<Health>();
        attack = GetComponent<PlayerAttack>();
        caster = GetComponent<MagicCaster>();
        meter = GetComponent<SpecialMeter>();
        ultimate = GetComponent<UltimateAbility>();
        ownBoss = GetComponent<BossAI>();
        battleBoss = ownBoss != null ? ownBoss : FindAnyObjectByType<BossAI>();
        bossHealth = battleBoss != null ? battleBoss.GetComponent<Health>() : null;
        playerHealth = PlayerController.Instance != null ? PlayerController.Instance.GetComponent<Health>() : null;

        voice = CreateSource(false);
        loop = CreateSource(true);
        lowHealthLoop = CreateSource(true);
        musicCurrent = CreateSource(false);
        musicPrevious = CreateSource(false);

        Subscribe(true);
        PlayMusic(startingMusic, true);
    }

    void OnDestroy()
    {
        Subscribe(false);
    }

    // Hooks up (or unhooks) every event this object's components send
    private void Subscribe(bool on)
    {
        if (health != null)
        {
            if (on) { health.Died += OnDefeated; health.Changed += OnHealthChanged; lastHealth = health.Current; }
            else { health.Died -= OnDefeated; health.Changed -= OnHealthChanged; }
        }
        if (body != null)
        {
            if (on) { body.Jumped += OnJumped; body.DoubleJumped += OnDoubleJumped; }
            else { body.Jumped -= OnJumped; body.DoubleJumped -= OnDoubleJumped; }
        }
        if (attack != null)
        {
            if (on) { attack.AttackStarted += OnAttackStarted; attack.AttackLanded += OnAttackLanded; }
            else { attack.AttackStarted -= OnAttackStarted; attack.AttackLanded -= OnAttackLanded; }
        }
        if (caster != null)
        {
            if (on) { caster.CastStarted += OnCastStarted; caster.Fired += OnFired; caster.ProjectileHit += OnProjectileHit; }
            else { caster.CastStarted -= OnCastStarted; caster.Fired -= OnFired; caster.ProjectileHit -= OnProjectileHit; }
        }
        if (meter != null)
        {
            if (on) { meter.ChargeStarted += OnChargeStarted; meter.ChargeStopped += OnChargeStopped; meter.BecameFull += OnGaugeFull; }
            else { meter.ChargeStarted -= OnChargeStarted; meter.ChargeStopped -= OnChargeStopped; meter.BecameFull -= OnGaugeFull; }
        }
        if (ultimate != null)
        {
            if (on) { ultimate.Started += OnUltimate; ultimate.Blasted += OnUltimateBlast; }
            else { ultimate.Started -= OnUltimate; ultimate.Blasted -= OnUltimateBlast; }
        }
        if (ownBoss != null)
        {
            if (on)
            {
                ownBoss.TeleportStarted += OnTeleport;
                ownBoss.EnteredPhase2 += OnPhase2;
                ownBoss.HealStarted += OnBossHeal;
                ownBoss.ShockwaveStarted += OnShockwave;
            }
            else
            {
                ownBoss.TeleportStarted -= OnTeleport;
                ownBoss.EnteredPhase2 -= OnPhase2;
                ownBoss.HealStarted -= OnBossHeal;
                ownBoss.ShockwaveStarted -= OnShockwave;
            }
        }
        if (battleBoss != null && body != null)
        {
            if (on) { battleBoss.BattleBegan += OnBattleBegan; }
            else { battleBoss.BattleBegan -= OnBattleBegan; }
        }

        // Characters only: everyone else with health is an opponent; when they go down, this one wins
        if (on && body != null)
        {
            opponents = System.Array.FindAll(FindObjectsByType<Health>(), h => h != health);
        }
        foreach (Health opponent in opponents)
        {
            if (opponent == null)
            {
                continue;
            }
            if (on) { opponent.Died += OnOpponentDefeated; }
            else { opponent.Died -= OnOpponentDefeated; }
        }
    }

    void Update()
    {
        UpdateMusic();
        UpdateVoiceQueue();
    }

    // ---------- Music ----------

    private void UpdateMusic()
    {
        // Pick the track for where the fight is (empty slots keep the current track)
        if (playerHealth != null && playerHealth.IsDead)
        {
            PlayMusic(defeatMusic, false);
        }
        else if (bossHealth != null && bossHealth.IsDead)
        {
            PlayMusic(victoryMusic, false);
        }
        else if (battleBoss != null && battleBoss.InPhase2)
        {
            PlayMusic(phase2Music != null ? phase2Music : bossMusic, true);
        }
        else if (battleBoss != null && battleBoss.BattleStarted)
        {
            PlayMusic(bossMusic, true);
        }

        // Crossfade in real time, so it keeps going while paused
        if (musicFade < 1f)
        {
            musicFade = musicFadeTime > 0f ? Mathf.MoveTowards(musicFade, 1f, Time.unscaledDeltaTime / musicFadeTime) : 1f;
        }
        float master = musicVolume * (Time.timeScale == 0f ? pausedMusicVolume : 1f);
        musicCurrent.volume = master * musicFade;
        musicPrevious.volume = master * (1f - musicFade);
        if (musicFade >= 1f && musicPrevious.isPlaying)
        {
            musicPrevious.Stop();
        }
    }

    // Fades to a track. Does nothing for an empty slot or the track already playing.
    private void PlayMusic(AudioClip clip, bool loopTrack)
    {
        if (clip == null || clip == wantedMusic)
        {
            return;
        }
        wantedMusic = clip;
        (musicCurrent, musicPrevious) = (musicPrevious, musicCurrent); // the playing track fades out
        musicCurrent.clip = clip;
        musicCurrent.loop = loopTrack;
        musicCurrent.volume = 0f;
        musicCurrent.Play();
        musicFade = musicFadeTime > 0f ? 0f : 1f;
    }

    // ---------- Menu & game sounds ----------

    private void OnOrbDropped() { orbDropSound.Play(); }
    private void OnOrbPickedUp() { orbPickupSound.Play(); }

    // ---------- Sound effects ----------

    private void OnJumped() { jumpSound.Play(); }
    private void OnDoubleJumped() { doubleJumpSound.Play(); }
    private void OnAttackStarted(int n) { PlayNumbered(attackSwingSounds, n); }
    private void OnAttackLanded(int n) { PlayNumbered(attackHitSounds, n); }
    private void OnCastStarted() { magicCastSound.Play(); }
    private void OnFired() { magicFireSound.Play(); }
    private void OnProjectileHit() { magicImpactSound.Play(); }
    private void OnGaugeFull() { gaugeFullSound.Play(); }
    private void OnUltimateBlast() { ultimateBlastSound.Play(); }

    private static void PlayNumbered(SoundEffect[] sounds, int attackNumber)
    {
        if (sounds != null && attackNumber >= 1 && attackNumber <= sounds.Length && sounds[attackNumber - 1] != null)
        {
            sounds[attackNumber - 1].Play();
        }
    }

    private void OnChargeStarted()
    {
        chargeStartSound.Play();
        if (chargeLoopSound.clip != null)
        {
            loop.clip = chargeLoopSound.clip;
            loop.volume = chargeLoopSound.volume * SoundPlayer.MasterVolume;
            loop.Play();
        }
    }

    private void OnChargeStopped()
    {
        loop.Stop();
    }

    // ---------- Voice lines ----------

    private void OnBattleBegan()
    {
        StartCoroutine(SayAfter(battleStartLine, battleStartDelay));
    }

    private void OnUltimate() { Say(ultimateLine); }
    private void OnTeleport() { Say(teleportLine); }
    private void OnPhase2() { Say(phase2Line, PhasePriority); } // beats the teleport line that comes right after it
    private void OnBossHeal() { Say(healLine); }
    private void OnShockwave() { Say(shockwaveLine); }

    private void OnHealthChanged(Health h)
    {
        float change = h.Current - lastHealth;
        lastHealth = h.Current;
        if (h.IsDead)
        {
            return;
        }
        if (!lowHealthPlayed && h.Fraction <= lowHealthAt)
        {
            lowHealthPlayed = true;
            Say(lowHealthLine);
            if (!loopLowHealthSound)
            {
                lowHealthSound.Play();
            }
        }
        else if (change < 0f)
        {
            Say(hurtLine, GruntPriority);
        }
        else if (change > 0f && ownBoss == null)
        {
            Say(healLine, GruntPriority); // the boss heals gradually, so its line plays from OnBossHeal instead
        }
        UpdateLowHealthLoop(h.Fraction <= lowHealthAt);
        if (lowHealthPlayed && h.Fraction > lowHealthAt + lowHealthResetMargin)
        {
            lowHealthPlayed = false; // healed back up: ready to warn again
        }
    }

    // Starts the looping low health sound when health is low, stops it when it isn't
    private void UpdateLowHealthLoop(bool low)
    {
        if (!loopLowHealthSound || lowHealthSound.clip == null)
        {
            return;
        }
        if (low && !lowHealthLoop.isPlaying)
        {
            lowHealthLoop.clip = lowHealthSound.clip;
            lowHealthLoop.volume = lowHealthSound.volume * SoundPlayer.MasterVolume;
            lowHealthLoop.Play();
        }
        else if (!low && lowHealthLoop.isPlaying)
        {
            lowHealthLoop.Stop();
        }
    }

    private void OnDefeated(Health h)
    {
        StopAllCoroutines(); // no battle line or victory after going down
        loop.Stop();
        UpdateLowHealthLoop(false);
        queuedClip = null; // nothing waiting gets said after going down
        Say(defeatLine, FinalPriority);
    }

    private void OnOpponentDefeated(Health h)
    {
        if (won || body == null || (health != null && health.IsDead))
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
        UpdateLowHealthLoop(false); // won: no more heartbeat
        queuedClip = null;
        Say(victoryLine, FinalPriority);
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

    // Plays a line on this character's voice.
    //   A line as important or more important than the one playing cuts it off.
    //   A less important line waits its turn: it plays as soon as the current line ends (if that's within
    //   Queued Line Max Wait seconds). Only one line waits; a more important one replaces it.
    //   Grunts (hurt / heal) never cut anything off or wait: they're skipped while another line plays.
    private void Say(SoundEffect line, int priority = NormalPriority)
    {
        bool busy = voice.isPlaying;
        if (busy && priority == GruntPriority)
        {
            return;
        }
        AudioClip picked = line != null ? line.TryPick() : null; // Clip or a random Variation
        if (picked == null) // no clip, or held back by its Cooldown / Chance / Max Plays
        {
            return;
        }
        if (busy && priority < currentPriority)
        {
            if (queuedClip == null || priority >= queuedPriority)
            {
                queuedClip = picked;
                queuedVolume = line.volume;
                queuedPriority = priority;
                queuedUntil = Time.unscaledTime + queuedLineMaxWait;
            }
            return;
        }
        PlayVoice(picked, line.volume, priority);
    }

    private void PlayVoice(AudioClip clip, float volume, int priority)
    {
        currentPriority = priority;
        voice.Stop();
        voice.clip = clip;
        voice.volume = volume * SoundPlayer.MasterVolume;
        voice.pitch = 1f; // voices always play at their recorded speed and pitch (Pitch Variation is ignored here)
        voice.Play();
    }

    // A waiting line plays once the voice is free (dropped if it waited too long to still make sense)
    private void UpdateVoiceQueue()
    {
        if (queuedClip == null || voice.isPlaying)
        {
            return;
        }
        if (Time.unscaledTime <= queuedUntil)
        {
            PlayVoice(queuedClip, queuedVolume, queuedPriority);
        }
        queuedClip = null;
    }

    private AudioSource CreateSource(bool looping)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = looping;
        source.spatialBlend = 0f;
        return source;
    }
}
