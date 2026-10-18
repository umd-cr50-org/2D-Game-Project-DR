using UnityEngine;

// One sound effect slot for the Inspector: drag an audio clip in, set its volume, and a little random pitch
// so repeated sounds (footsteps, jabs) don't sound identical. Empty slots are silent, so every slot is optional.
// Scripts call Play(); the sound is 2D (same volume wherever it happens on screen).
[System.Serializable]
public class SoundEffect
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Random pitch up/down each time it plays (0 = always the same). Ignored by voice lines.")]
    [Range(0f, 0.5f)] public float pitchVariation = 0.05f;

    [Header ("Limits (stop it being spammed)")]
    [Tooltip("Minimum seconds between plays. Tries during the cooldown are skipped. 0 = no cooldown.")]
    [Min(0f)] public float cooldown = 0f;
    [Tooltip("Chance it plays each time it's triggered (1 = always, 0.3 = about 1 in 3).")]
    [Range(0f, 1f)] public float chance = 1f;
    [Tooltip("Most times it can play per fight (until the scene restarts). 0 = no limit.")]
    [Min(0)] public int maxPlays = 0;

    [System.NonSerialized] private float lastPlayTime = float.NegativeInfinity;
    [System.NonSerialized] private int timesPlayed;

    public void Play()
    {
        if (TryUse())
        {
            SoundPlayer.Play(clip, volume, 1f + Random.Range(-pitchVariation, pitchVariation));
        }
    }

    // Checks the clip and the limits; if it may play, counts it as played and returns true.
    // (Voice lines call this too, so the limits work for them as well.)
    public bool TryUse()
    {
        if (clip == null)
        {
            return false;
        }
        float now = Time.unscaledTime; // real time, so cooldowns still run while the game is paused
        if (now - lastPlayTime < cooldown || (maxPlays > 0 && timesPlayed >= maxPlays) || Random.value > chance)
        {
            return false;
        }
        lastPlayTime = now;
        timesPlayed++;
        return true;
    }
}

// Plays sound effects through a small pool of 2D audio sources that survive scene changes
// (so a button click still finishes when the button loads another scene).
public static class SoundPlayer
{
    public static float MasterVolume = 1f; // scales every sound effect

    private const int PoolSize = 12;
    private static AudioSource[] pool;
    private static int next;

    public static void Play(AudioClip clip, float volume, float pitch)
    {
        if (clip == null)
        {
            return;
        }
        EnsurePool();
        AudioSource source = pool[next];
        next = (next + 1) % PoolSize;
        source.pitch = pitch;
        source.PlayOneShot(clip, volume * MasterVolume);
    }

    private static void EnsurePool()
    {
        if (pool != null && pool[0] != null)
        {
            return;
        }
        GameObject go = new GameObject("Sound Effects");
        Object.DontDestroyOnLoad(go);
        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            pool[i] = go.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
            pool[i].spatialBlend = 0f; // 2D
        }
    }
}
