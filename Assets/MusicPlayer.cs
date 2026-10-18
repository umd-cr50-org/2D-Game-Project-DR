using UnityEngine;

// Background music. Put one in each scene (on any object, e.g. an empty "Music" object) and drag tracks in.
// It fades between tracks by itself as the fight goes on:
//   Starting Music -> Boss Music (battle starts) -> Phase 2 Music (boss at half health)
//   -> Victory Music (boss defeated) or Defeat Music (player defeated).
// Any track left empty is skipped (the current one keeps playing). The main menu just uses Starting Music.
public class MusicPlayer : MonoBehaviour
{
    [Header ("Tracks (all optional)")]
    [Tooltip("Plays as soon as the scene starts (menu music / area music before the boss).")]
    [SerializeField] private AudioClip startingMusic;
    [SerializeField] private AudioClip bossMusic;
    [SerializeField] private AudioClip phase2Music;
    [Tooltip("Plays once (no loop).")]
    [SerializeField] private AudioClip victoryMusic;
    [Tooltip("Plays once (no loop).")]
    [SerializeField] private AudioClip defeatMusic;

    [Header ("Volume")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
    [Tooltip("Music volume is multiplied by this while the game is paused.")]
    [SerializeField, Range(0f, 1f)] private float pausedVolume = 0.4f;
    [Tooltip("Seconds to fade from one track to the next.")]
    [SerializeField] private float fadeTime = 1.5f;

    private AudioSource current, previous;
    private float fade = 1f; // 0..1 progress of the current crossfade
    private BossAI boss;
    private Health bossHealth;
    private Health playerHealth;
    private AudioClip wanted;

    void Awake()
    {
        current = CreateSource();
        previous = CreateSource();
    }

    void Start()
    {
        boss = FindAnyObjectByType<BossAI>();
        bossHealth = boss != null ? boss.GetComponent<Health>() : null;
        playerHealth = PlayerController.Instance != null ? PlayerController.Instance.GetComponent<Health>() : null;
        PlayTrack(startingMusic, loop: true);
    }

    void Update()
    {
        // Pick the track for where the fight is
        if (playerHealth != null && playerHealth.IsDead)
        {
            PlayTrack(defeatMusic, loop: false);
        }
        else if (bossHealth != null && bossHealth.IsDead)
        {
            PlayTrack(victoryMusic, loop: false);
        }
        else if (boss != null && boss.InPhase2)
        {
            PlayTrack(phase2Music != null ? phase2Music : bossMusic, loop: true);
        }
        else if (boss != null && boss.BattleStarted)
        {
            PlayTrack(bossMusic, loop: true);
        }

        // Crossfade (in real time, so it keeps going while paused)
        if (fade < 1f)
        {
            fade = fadeTime > 0f ? Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / fadeTime) : 1f;
        }
        float master = volume * (Time.timeScale == 0f ? pausedVolume : 1f);
        current.volume = master * fade;
        previous.volume = master * (1f - fade);
        if (fade >= 1f && previous.isPlaying)
        {
            previous.Stop();
        }
    }

    // Fades to a track. Does nothing for an empty slot or the track already playing.
    public void PlayTrack(AudioClip clip, bool loop)
    {
        if (clip == null || clip == wanted)
        {
            return;
        }
        wanted = clip;

        // The playing track becomes the one fading out
        (current, previous) = (previous, current);
        current.clip = clip;
        current.loop = loop;
        current.volume = 0f;
        current.Play();
        fade = fadeTime > 0f ? 0f : 1f; // the new track fades in while the old one fades out (or from silence)
    }

    private AudioSource CreateSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }
}
