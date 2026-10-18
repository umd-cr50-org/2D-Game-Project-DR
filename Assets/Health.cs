using System;
using UnityEngine;

// Health for any character (player or boss). Attacks and spells hit it through IHittable:
//   damage -> blinks red -> knockback + hit stun (the character can't act for Hit Stun Time) -> brief invincibility.
// At 0 health the character is defeated: it stops acting and plays its Defeat Animation.
// The HUD (GameUI) reads Current / Fraction and listens to the events below.
[RequireComponent(typeof(PlayerController))]
public class Health : MonoBehaviour, IHittable
{
    [Header ("Health")]
    [SerializeField, Min(1f)] private float maxHealth = 30f;

    [Header ("Getting Hit")]
    [Tooltip("How long the character can't act after a hit. 0 = super armor: it flashes but keeps going (no knockback either).")]
    [SerializeField] private float hitStunTime = 0.3f;
    [Tooltip("Scales the knockback of every hit taken (0 = never pushed).")]
    [SerializeField] private float knockbackMultiplier = 1f;
    [Tooltip("After a hit, further hits are ignored for this long.")]
    [SerializeField] private float invincibilityTime = 0.6f;

    [Header ("Damage Blink")]
    [SerializeField] private Color blinkColor = new Color(1f, 0.2f, 0.2f, 1f);
    [Tooltip("How long one red blink lasts. The sprite blinks red / normal for the whole invincibility time (at least one blink).")]
    [SerializeField] private float blinkInterval = 0.08f;

    [Header ("Defeat")]
    [Tooltip("Animator state played when health reaches 0 (e.g. Player_Lose / Boss_Lose).")]
    [SerializeField] private string defeatAnimation = "";

    [Header ("Sounds")]
    [SerializeField] private SoundEffect hurtSound = new SoundEffect();
    [SerializeField] private SoundEffect defeatSound = new SoundEffect();
    [SerializeField] private SoundEffect healSound = new SoundEffect();

    public float Max => maxHealth;
    public float Current { get; private set; }
    public float Fraction => Current / maxHealth;
    public bool IsDead { get; private set; }
    public bool Invulnerable { get; set; } // ignores all hits while on (during an ultimate)

    public event Action<Health> Changed; // damage taken (or healed)
    public event Action<Health> Died;
    // Every hit that lands on any character, anywhere (the special meter listens to this)
    public static event Action<Health, AttackHitInfo> AnyHit;

    private PlayerController body;
    private SpriteRenderer sr;
    private float invincibleUntil = -1f;
    private float blinkUntil = -1f;
    private bool blinking;

    void Awake()
    {
        Current = maxHealth;
        body = GetComponent<PlayerController>();
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    void Update()
    {
        UpdateBlink();
    }

    public void OnHit(AttackHitInfo hit)
    {
        bool invincible = Time.time < invincibleUntil && !hit.ignoresInvincibility;
        if (IsDead || Invulnerable || invincible || hit.damage <= 0f)
        {
            return;
        }

        Current = Mathf.Max(0f, Current - hit.damage);
        invincibleUntil = Time.time + invincibilityTime;
        blinkUntil = Time.time + Mathf.Max(invincibilityTime, blinkInterval * 2f);
        AnyHit?.Invoke(this, hit);
        Changed?.Invoke(this);

        if (Current <= 0f)
        {
            Die();
            return;
        }
        hurtSound.Play();
        // Some hits (the boss's shockwave) stun longer than usual; super armor (0) still ignores stun entirely
        if (hitStunTime > 0f)
        {
            body.TakeHit(hit.knockback * knockbackMultiplier, Mathf.Max(hitStunTime, hit.hitStun));
        }
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f)
        {
            return;
        }
        Current = Mathf.Min(maxHealth, Current + amount);
        healSound.Play();
        Changed?.Invoke(this);
    }

    private void Die()
    {
        IsDead = true;
        defeatSound.Play();
        body.Defeat(defeatAnimation);
        Died?.Invoke(this);
    }

    // Alternates red / normal every blinkInterval until the blink time is up
    private void UpdateBlink()
    {
        if (sr == null)
        {
            return;
        }
        if (Time.time < blinkUntil)
        {
            blinking = true;
            bool red = Mathf.FloorToInt((blinkUntil - Time.time) / blinkInterval) % 2 == 1;
            sr.color = red ? blinkColor : body.BaseColor;
        }
        else if (blinking)
        {
            blinking = false;
            sr.color = body.BaseColor; // back to normal (or the free fall tint)
        }
    }
}
