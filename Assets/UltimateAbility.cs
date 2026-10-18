using UnityEngine;

// The ultimate: when the magic gauge (SpecialMeter) is full, press the ultimate button (V for the player;
// the boss decides with BossAI) to spend all of it on a huge blast around the character:
//   wind-up (a growing circle shows the blast area, the character can't be hurt) -> BLAST -> recovery.
// Everything inside the circle takes the damage and is launched away from the character.
// Damage = Flat Damage + Target Max Health Fraction x the target's max health
// (the boss's is 0.5 of the player's max health: half their health bar, however much health they're given).
// Adding this to the boss in the editor fills in boss settings automatically.
[RequireComponent(typeof(PlayerController), typeof(SpecialMeter))]
public class UltimateAbility : MonoBehaviour
{
    [Header ("Timing")]
    [Tooltip("Wind-up before the blast. The circle grows over this time, so it's the warning for the other side.")]
    [SerializeField] private float windup = 0.6f;
    [Tooltip("Time after the blast before the character can act again.")]
    [SerializeField] private float recovery = 0.5f;
    [Tooltip("Can't be hurt or interrupted during the whole ultimate.")]
    [SerializeField] private bool invulnerable = true;

    [Header ("Blast")]
    [Tooltip("Radius of the blast (world units, from the center of the character's body).")]
    [SerializeField] private float radius = 18f;
    [SerializeField] private float flatDamage = 15f;
    [Tooltip("Extra damage as a fraction of each target's max health (0.5 = half their health bar).")]
    [SerializeField, Range(0f, 1f)] private float targetMaxHealthFraction = 0f;
    [Tooltip("X pushes targets away from the character, Y launches them up.")]
    [SerializeField] private Vector2 knockback = new Vector2(45f, 30f);
    [Tooltip("How long targets are helpless after being hit.")]
    [SerializeField] private float hitStun = 0.6f;

    [Header ("Look")]
    [Tooltip("Animator state played for the ultimate (e.g. Player_Limit_Break / Boss_Attack4).")]
    [SerializeField] private string animationState = "Player_Limit_Break";
    [Tooltip("Animator state played when it's over (e.g. Player_Idle / Boss_Idle).")]
    [SerializeField] private string endAnimation = "Player_Idle";
    [SerializeField] private Color warningColor = new Color(0.4f, 0.75f, 1f, 0.25f);
    [SerializeField] private Color blastColor = new Color(0.7f, 0.9f, 1f, 0.85f);

    [Header ("Sounds")]
    [SerializeField] private SoundEffect windupSound = new SoundEffect();
    [SerializeField] private SoundEffect blastSound = new SoundEffect();

    public bool IsActive { get; private set; }
    public bool IsReady => !IsActive && meter != null && meter.IsFull;
    public float Radius => radius;
    public event System.Action Started; // the ultimate just began (CharacterVoice plays its line)

    private PlayerController body;
    private SpecialMeter meter;
    private Health health;
    private PlayerAttack attack;
    private MagicCaster caster;
    private Animator anim;
    private Collider2D bodyCollider;
    private SpriteRenderer effect;
    private float startTime;
    private bool blasted;

    // Called by Unity when the component is added in the editor: boss gets boss settings
    void Reset()
    {
        if (GetComponent<BossAI>() == null)
        {
            return;
        }
        windup = 1.2f;
        recovery = 0.6f;
        radius = 22f;
        flatDamage = 0f;
        targetMaxHealthFraction = 0.5f;
        knockback = new Vector2(50f, 35f);
        animationState = "Boss_Attack4";
        endAnimation = "Boss_Idle";
        warningColor = new Color(1f, 0.25f, 0.2f, 0.25f);
        blastColor = new Color(1f, 0.45f, 0.3f, 0.85f);
    }

    void Start()
    {
        body = GetComponent<PlayerController>();
        meter = GetComponent<SpecialMeter>();
        health = GetComponent<Health>();
        attack = GetComponent<PlayerAttack>();
        caster = GetComponent<MagicCaster>();
        anim = GetComponent<Animator>();
        bodyCollider = GetComponent<Collider2D>();
    }

    void Update()
    {
        if (!IsActive)
        {
            CharacterInput input = body.InputSource;
            if (input != null && input.UltimatePressed && CanStart())
            {
                Begin();
            }
            return;
        }

        if (body.IsDefeated)
        {
            End();
            return;
        }

        float t = Time.time - startTime;
        if (!blasted)
        {
            // Warning circle grows to the full blast size during the wind-up
            ShowEffect(warningColor, Mathf.Clamp01(t / Mathf.Max(windup, 0.01f)));
            if (t >= windup)
            {
                Blast();
            }
            return;
        }

        // Blast flash fades out during recovery
        float fadeOut = 1f - Mathf.Clamp01((t - windup) / Mathf.Max(recovery, 0.01f));
        Color c = blastColor;
        c.a *= fadeOut;
        ShowEffect(c, 1f);
        if (t >= windup + recovery)
        {
            End();
        }
    }

    private bool CanStart()
    {
        return IsReady && body.CanAct && !body.IsFreeFalling
            && (attack == null || !attack.IsAttacking)
            && (caster == null || !caster.IsCasting);
    }

    private void Begin()
    {
        meter.StopCharging();
        meter.Spend();
        body.CancelDash();
        IsActive = true;
        blasted = false;
        startTime = Time.time;
        if (health != null && invulnerable)
        {
            health.Invulnerable = true;
        }
        PlayState(animationState);
        windupSound.Play();
        Started?.Invoke();
    }

    private void Blast()
    {
        blasted = true;
        blastSound.Play();

        Vector2 center = Center();
        System.Collections.Generic.HashSet<IHittable> done = new System.Collections.Generic.HashSet<IHittable>();
        foreach (Collider2D col in Physics2D.OverlapCircleAll(center, radius))
        {
            if (col.transform.IsChildOf(transform))
            {
                continue;
            }
            IHittable target = col.GetComponentInParent<IHittable>();
            if (target == null || !done.Add(target))
            {
                continue;
            }

            Health targetHealth = (target as Component)?.GetComponent<Health>();
            float damage = flatDamage + (targetHealth != null ? targetMaxHealthFraction * targetHealth.Max : 0f);
            float side = Mathf.Sign(col.bounds.center.x - center.x);
            target.OnHit(new AttackHitInfo
            {
                attackNumber = 0,
                damage = damage,
                knockback = new Vector2(side * knockback.x, knockback.y),
                attacker = gameObject,
                hitStun = hitStun,
                ignoresInvincibility = true,
            });
        }
    }

    private void End()
    {
        IsActive = false;
        if (health != null)
        {
            health.Invulnerable = false;
        }
        if (effect != null)
        {
            effect.enabled = false;
        }
        if (!body.IsDefeated)
        {
            PlayState(endAnimation);
        }
    }

    private Vector2 Center()
    {
        return bodyCollider != null ? (Vector2)bodyCollider.bounds.center : (Vector2)transform.position;
    }

    // The circle: a soft orb sized to the blast radius (scaled by progress), drawn in front of the stage
    private void ShowEffect(Color color, float progress)
    {
        if (effect == null)
        {
            GameObject go = new GameObject("Ultimate Effect");
            effect = go.AddComponent<SpriteRenderer>();
            effect.sprite = MagicCaster.GetOrbSprite();
            effect.sortingOrder = 45;
        }
        effect.enabled = true;
        effect.color = color;
        effect.transform.position = Center();
        effect.transform.localScale = Vector3.one * (radius * 2f * Mathf.Max(progress, 0.05f));
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
            anim.speed = 1f;
            anim.Play(hash, 0, 0f);
            anim.Update(0f);
        }
    }

    void OnDestroy()
    {
        if (effect != null)
        {
            Destroy(effect.gameObject);
        }
    }

    // Blast area when the character is selected
    void OnDrawGizmosSelected()
    {
        Collider2D c = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
        Vector3 center = c != null ? c.bounds.center : transform.position;
        Gizmos.color = new Color(blastColor.r, blastColor.g, blastColor.b, 1f);
        Gizmos.DrawWireSphere(center, radius);
    }
}
