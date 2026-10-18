using UnityEngine;

// Put on the boss: every time the player deals Drop Every Damage to it, a health orb pops out of it.
// The orb lands on the ground and heals the player when they touch it (see HealthPickup).
[RequireComponent(typeof(Health))]
public class HealthDropper : MonoBehaviour
{
    [Tooltip("A health orb drops each time this much damage has been dealt to this character.")]
    [SerializeField] private float dropEveryDamage = 12f;
    [Tooltip("Health each orb gives back to the player.")]
    [SerializeField] private float healAmount = 5f;
    [Tooltip("Seconds an orb stays before disappearing (it blinks near the end).")]
    [SerializeField] private float orbLifetime = 10f;
    [Tooltip("How hard the orb pops out: X sideways (random side), Y up.")]
    [SerializeField] private Vector2 popSpeed = new Vector2(15f, 30f);

    [Header ("Look (used when no prefab is set)")]
    [Tooltip("Optional: a prefab with a HealthPickup (plus Rigidbody2D and Collider2D). Leave empty for a glowing green orb.")]
    [SerializeField] private HealthPickup pickupPrefab;
    [Tooltip("Diameter of the default orb (world units).")]
    [SerializeField] private float orbSize = 2.5f;
    [SerializeField] private Color orbColor = new Color(0.3f, 1f, 0.4f);

    [Header ("Sounds")]
    [SerializeField] private SoundEffect dropSound = new SoundEffect();
    [Tooltip("Plays when the player picks an orb up.")]
    [SerializeField] private SoundEffect pickupSound = new SoundEffect();

    private Health health;
    private Collider2D bodyCollider;
    private float lastHealth;
    private float damageSinceDrop;

    void Awake()
    {
        health = GetComponent<Health>();
        bodyCollider = GetComponent<Collider2D>();
    }

    void OnEnable()
    {
        health.Changed += OnHealthChanged;
    }

    void OnDisable()
    {
        health.Changed -= OnHealthChanged;
    }

    void Start()
    {
        lastHealth = health.Current;
    }

    private void OnHealthChanged(Health h)
    {
        float lost = lastHealth - h.Current;
        lastHealth = h.Current;
        if (lost <= 0f || h.IsDead || dropEveryDamage <= 0f)
        {
            return;
        }
        damageSinceDrop += lost;
        while (damageSinceDrop >= dropEveryDamage)
        {
            damageSinceDrop -= dropEveryDamage;
            Drop();
        }
    }

    private void Drop()
    {
        // From the top of the body, popping up and off to a random side
        Vector2 position = bodyCollider != null
            ? new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.max.y)
            : (Vector2)transform.position;
        HealthPickup pickup = pickupPrefab != null
            ? Instantiate(pickupPrefab, position, Quaternion.identity)
            : HealthPickup.CreateDefault(position, orbSize, orbColor);
        pickup.Setup(healAmount, orbLifetime, pickupSound);
        dropSound.Play();

        Rigidbody2D rb = pickup.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            rb.linearVelocity = new Vector2(side * popSpeed.x * Random.Range(0.5f, 1f), popSpeed.y);
        }
    }
}
