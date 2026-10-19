using System;
using UnityEngine;

// The magic gauge: fills up as the character deals damage and as they take it, and while they charge it
// (hold the charge button, C for the player: they stand still and glow until they let go, get hit, or it's full).
// Spent by magic (MagicCaster's Meter Cost) and, all of it, by the ultimate (UltimateAbility).
// Shown on the HUD (GameUI). The boss can have one too: it fills the same way and powers the boss's ultimate.
[RequireComponent(typeof(Health))]
public class SpecialMeter : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxMeter = 100f;
    [Tooltip("Meter gained per point of damage this character deals (attacks).")]
    [SerializeField] private float gainPerDamageDealt = 4f;
    [Tooltip("Meter gained per point of damage this character takes.")]
    [SerializeField] private float gainPerDamageTaken = 3f;
    [Tooltip("Whether this character's own magic hits fill the meter. Off = magic only spends it (otherwise a hit could refund the cast).")]
    [SerializeField] private bool gainFromOwnMagic = false;

    [Header ("Charging (hold the charge button)")]
    [Tooltip("Meter gained per second while charging.")]
    [SerializeField] private float chargeRate = 10f;
    [Tooltip("Optional animator state played while charging (e.g. Player_Limit_Break_Idle).")]
    [SerializeField] private string chargeAnimation = "";
    [Tooltip("Animator state played when charging stops (e.g. Player_Idle), so the charge pose doesn't stick.")]
    [SerializeField] private string chargeEndAnimation = "Player_Idle";
    [Tooltip("Color of the glow around the character while charging.")]
    [SerializeField] private Color chargeGlowColor = new Color(0.4f, 0.75f, 1f, 0.6f);
    [Tooltip("Size of the glow compared to the character's body.")]
    [SerializeField] private float chargeGlowSize = 1.6f;

    public float Max => maxMeter;
    public float Current { get; private set; }
    public float Fraction => Current / maxMeter;
    public bool IsFull => Current >= maxMeter;
    public bool IsCharging { get; private set; }

    public event Action<SpecialMeter> Changed;
    public event Action ChargeStarted; // GameAudio plays the charge start sound and loop
    public event Action ChargeStopped;
    public event Action BecameFull; // the gauge just filled up

    private Health health;
    private PlayerController body;
    private PlayerAttack attack;
    private MagicCaster caster;
    private UltimateAbility ultimate;
    private Animator anim;
    private Collider2D bodyCollider;
    private SpriteRenderer glow;

    void Awake()
    {
        health = GetComponent<Health>();
        body = GetComponent<PlayerController>();
    }

    void Start()
    {
        attack = GetComponent<PlayerAttack>();
        caster = GetComponent<MagicCaster>();
        ultimate = GetComponent<UltimateAbility>();
        anim = GetComponent<Animator>();
        bodyCollider = GetComponent<Collider2D>();
    }

    void OnEnable()
    {
        Health.AnyHit += OnAnyHit;
    }

    void OnDisable()
    {
        Health.AnyHit -= OnAnyHit;
    }

    void Update()
    {
        bool wantsCharge = body.InputSource != null && body.InputSource.ChargeHeld;
        if (IsCharging)
        {
            if (!wantsCharge || IsFull || !CanCharge())
            {
                StopCharging();
                return;
            }
            Add(chargeRate * Time.deltaTime);
            UpdateGlow();
        }
        else if (wantsCharge && !IsFull && CanCharge())
        {
            StartCharging();
        }
    }

    // Standing on the ground and doing nothing else (attacking, casting or the ultimate ends a charge)
    private bool CanCharge()
    {
        return body.CanAct && body.IsGrounded && !body.IsDashing
            && (attack == null || !attack.IsAttacking)
            && (caster == null || !caster.IsCasting)
            && (ultimate == null || !ultimate.IsActive);
    }

    private void StartCharging()
    {
        IsCharging = true;
        ChargeStarted?.Invoke();
        body.SetAnimBool("Charging", true);
        PlayState(chargeAnimation);
        UpdateGlow();
    }

    // Called when charging ends for any reason (let go, full, hit - PlayerController.Interrupt calls this)
    public void StopCharging()
    {
        if (!IsCharging)
        {
            return;
        }
        IsCharging = false;
        ChargeStopped?.Invoke();
        body.SetAnimBool("Charging", false);
        if (!string.IsNullOrEmpty(chargeAnimation) && !body.IsDefeated)
        {
            PlayState(chargeEndAnimation);
        }
        if (glow != null)
        {
            glow.enabled = false;
        }
    }

    private void OnAnyHit(Health victim, AttackHitInfo hit)
    {
        if (victim == health)
        {
            Add(hit.damage * gainPerDamageTaken);
        }
        else if (hit.attacker == gameObject && (hit.attackNumber != 0 || gainFromOwnMagic))
        {
            Add(hit.damage * gainPerDamageDealt);
        }
    }

    public void Add(float amount)
    {
        float before = Current;
        Current = Mathf.Clamp(Current + amount, 0f, maxMeter);
        if (Current == before)
        {
            return;
        }
        if (Current >= maxMeter && before < maxMeter)
        {
            BecameFull?.Invoke();
        }
        Changed?.Invoke(this);
    }

    // Uses the whole meter. Returns false (and keeps the meter) if it isn't full yet.
    public bool Spend()
    {
        if (!IsFull)
        {
            return false;
        }
        Current = 0f;
        Changed?.Invoke(this);
        return true;
    }

    // A soft pulsing glow behind the character while charging
    private void UpdateGlow()
    {
        if (glow == null)
        {
            GameObject go = new GameObject("Charge Glow");
            go.transform.SetParent(transform, false);
            glow = go.AddComponent<SpriteRenderer>();
            glow.sprite = MagicCaster.GetOrbSprite();
            SpriteRenderer bodySprite = GetComponentInChildren<SpriteRenderer>();
            glow.sortingLayerID = bodySprite != null ? bodySprite.sortingLayerID : 0;
            glow.sortingOrder = bodySprite != null ? bodySprite.sortingOrder - 1 : -1;
        }
        glow.enabled = true;
        glow.color = chargeGlowColor;

        // Sized from the body in world units, then converted to local scale
        Bounds b = bodyCollider != null ? bodyCollider.bounds : new Bounds(transform.position, Vector3.one);
        float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 12f);
        float worldSize = Mathf.Max(b.size.x, b.size.y) * chargeGlowSize * pulse;
        Vector3 parentScale = transform.lossyScale;
        glow.transform.position = b.center;
        glow.transform.localScale = new Vector3(worldSize / Mathf.Abs(parentScale.x), worldSize / Mathf.Abs(parentScale.y), 1f);
    }

    private void PlayState(string stateName)
    {
        if (anim == null || anim.runtimeAnimatorController == null || string.IsNullOrEmpty(stateName))
        {
            return;
        }
        int hash = Animator.StringToHash(stateName);
        if (anim.HasState(0, hash))
        {
            anim.Play(hash, 0, 0f);
        }
    }
}
