using UnityEngine;

// Magic for any character (player or boss). Press the magic button (CharacterInput.MagicPressed:
// X for the player, decided by BossAI for the boss) to cast:
//   cast time (wind-up, the character stands still) -> the spell fires -> recovery -> cooldown.
// The spell is one or more Projectiles, fired the way the character faces, or at AimTarget if one is set
// (the boss aims at the player). With no projectile prefab assigned it makes a simple glowing orb.
// Animation: plays castStateName if the animator has it, and sets the "Casting" bool while casting.
// To fire on an exact animation frame instead of after castTime, tick fireFromAnimationEvent and add an
// Animation Event that calls FireMagic on that frame.
[RequireComponent(typeof(PlayerController))]
public class MagicCaster : MonoBehaviour
{
    [Header ("Casting")]
    [Tooltip("Wind-up before the spell comes out.")]
    [SerializeField] private float castTime = 0.35f;
    [Tooltip("Time after the spell comes out before the character can act again.")]
    [SerializeField] private float recoveryTime = 0.25f;
    [Tooltip("Minimum time between casts (counted from the start of a cast).")]
    [SerializeField] private float cooldown = 1.5f;
    [Tooltip("Fire when an Animation Event calls FireMagic instead of after Cast Time.")]
    [SerializeField] private bool fireFromAnimationEvent;

    [Header ("Projectile")]
    [Tooltip("Optional. A prefab with a Projectile script. Leave empty for a simple glowing orb.")]
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField] private float speed = 45f;
    [SerializeField] private float damage = 8f;
    [Tooltip("X is the way the projectile travels, Y is up.")]
    [SerializeField] private Vector2 knockback = new Vector2(6f, 4f);
    [SerializeField] private float lifetime = 2f;
    [Tooltip("Diameter of the hit area and of the default orb (world units).")]
    [SerializeField] private float size = 3f;
    [SerializeField] private Color orbColor = new Color(0.45f, 0.8f, 1f);
    [Tooltip("How many projectiles each cast fires.")]
    [SerializeField, Min(1)] private int count = 1;
    [Tooltip("Angle between projectiles when firing more than one (a fan).")]
    [SerializeField] private float spreadAngle = 15f;
    [Tooltip("Where the spell appears, from the character's pivot in its local units. +X is in front (flips with facing).")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(0.6f, 0.1f);

    [Header ("Animation")]
    [Tooltip("Animator state to play when casting (e.g. Boss_Magic). Leave empty for none.")]
    [SerializeField] private string castStateName = "";

    public bool IsCasting { get; private set; }
    public bool IsCastState(int stateHash) => !string.IsNullOrEmpty(castStateName) && Animator.StringToHash(castStateName) == stateHash;
    public bool IsReady => !IsCasting && Time.time >= nextCastTime;
    public Transform AimTarget { get; set; } // aim at this instead of straight ahead (the boss sets the player)

    private PlayerController body;
    private PlayerAttack attack;
    private Animator anim;
    private float fireTime;
    private float endTime;
    private float nextCastTime;
    private bool fired;
    private static Sprite orbSprite;

    void Start()
    {
        body = GetComponent<PlayerController>();
        attack = GetComponent<PlayerAttack>();
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        if (!IsCasting)
        {
            CharacterInput input = body.InputSource;
            if (input != null && input.MagicPressed && CanStartCast())
            {
                StartCast();
            }
            return;
        }

        if (!fired && !fireFromAnimationEvent && Time.time >= fireTime)
        {
            FireMagic();
        }
        if (Time.time >= endTime)
        {
            if (!fired)
            {
                FireMagic(); // the animation event never came: fire anyway so the cast isn't wasted
            }
            IsCasting = false;
            body.SetAnimBool("Casting", false);
        }
    }

    private bool CanStartCast()
    {
        return IsReady && !body.IsDashing && !body.IsFreeFalling && (attack == null || !attack.IsAttacking);
    }

    private void StartCast()
    {
        IsCasting = true;
        fired = false;
        fireTime = Time.time + castTime;
        endTime = fireTime + recoveryTime;
        nextCastTime = Time.time + cooldown;

        if (AimTarget != null)
        {
            body.Face(AimTarget.position.x - transform.position.x);
        }
        body.SetAnimBool("Casting", true);
        if (anim != null && !string.IsNullOrEmpty(castStateName))
        {
            int stateHash = Animator.StringToHash(castStateName);
            if (anim.HasState(0, stateHash))
            {
                anim.Play(stateHash, 0, 0f);
            }
        }
    }

    // Fires the spell. Called automatically after castTime, or by an Animation Event on the cast animation.
    public void FireMagic()
    {
        if (!IsCasting || fired)
        {
            return;
        }
        fired = true;

        float facing = body.FacingSign();
        Vector2 scale = new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        Vector2 origin = (Vector2)transform.position + new Vector2(spawnOffset.x * facing * scale.x, spawnOffset.y * scale.y);

        Vector2 aim = new Vector2(facing, 0f);
        if (AimTarget != null)
        {
            Collider2D targetCollider = AimTarget.GetComponent<Collider2D>();
            Vector2 targetPoint = targetCollider != null ? (Vector2)targetCollider.bounds.center : (Vector2)AimTarget.position;
            aim = (targetPoint - origin).normalized;
        }

        float baseAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + (i - (count - 1) * 0.5f) * spreadAngle;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            Projectile projectile = projectilePrefab != null
                ? Instantiate(projectilePrefab, origin, Quaternion.identity)
                : CreateDefaultOrb(origin);
            projectile.Launch(gameObject, direction, speed, damage, knockback, lifetime, size * 0.5f);
        }
    }

    // A plain glowing orb so magic works before any projectile art exists
    private Projectile CreateDefaultOrb(Vector2 position)
    {
        GameObject orb = new GameObject("Magic Orb");
        orb.transform.position = position;
        orb.transform.localScale = Vector3.one * size;
        SpriteRenderer sr = orb.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrbSprite();
        sr.color = orbColor;
        sr.sortingOrder = 50;
        return orb.AddComponent<Projectile>();
    }

    // A soft white circle 1 world unit across, generated once
    private static Sprite GetOrbSprite()
    {
        if (orbSprite != null)
        {
            return orbSprite;
        }
        const int resolution = 64;
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float center = (resolution - 1) * 0.5f;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                float alpha = Mathf.Clamp01(1f - distance);
                alpha = Mathf.SmoothStep(0f, 1f, alpha * 1.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        orbSprite = Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
        return orbSprite;
    }

    // Shows where spells appear when the character is selected
    void OnDrawGizmosSelected()
    {
        PlayerController controller = body != null ? body : GetComponent<PlayerController>();
        float facing = controller != null ? controller.FacingSign() : 1f;
        Vector2 scale = new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        Vector2 origin = (Vector2)transform.position + new Vector2(spawnOffset.x * facing * scale.x, spawnOffset.y * scale.y);
        Gizmos.color = orbColor;
        Gizmos.DrawWireSphere(origin, size * 0.5f);
        Gizmos.DrawLine(origin, origin + new Vector2(facing * size * 2f, 0f));
    }
}
