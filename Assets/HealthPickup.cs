using UnityEngine;

// A health orb (dropped by HealthDropper). It falls and rests on the ground, passes through characters,
// and heals the player when they touch it - only if they're missing health, otherwise it waits for them.
// Blinks for its last couple of seconds, then disappears.
public class HealthPickup : MonoBehaviour
{
    [SerializeField] private float healAmount = 5f;
    [SerializeField] private float lifetime = 10f;
    [Tooltip("Blinks for this many seconds before disappearing.")]
    [SerializeField] private float blinkTime = 2f;

    private float dieAt;
    private Collider2D col;
    private SpriteRenderer sr;

    public static event System.Action PickedUp; // any health orb picked up (GameAudio plays the pickup sound)

    public void Setup(float heal, float life)
    {
        healAmount = heal;
        lifetime = life;
        dieAt = Time.time + lifetime;
    }

    void Start()
    {
        if (dieAt <= 0f)
        {
            dieAt = Time.time + lifetime; // placed in the scene by hand
        }
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();

        // Characters walk through it (it only lands on the ground)
        if (col != null)
        {
            foreach (PlayerController character in FindObjectsByType<PlayerController>())
            {
                foreach (Collider2D other in character.GetComponentsInChildren<Collider2D>())
                {
                    Physics2D.IgnoreCollision(col, other);
                }
            }
        }
    }

    void Update()
    {
        if (Time.time >= dieAt)
        {
            Destroy(gameObject);
            return;
        }
        if (sr != null)
        {
            bool blinkingOut = dieAt - Time.time < blinkTime;
            sr.enabled = !blinkingOut || Mathf.FloorToInt(Time.time * 10f) % 2 == 0;
        }

        PlayerController player = PlayerController.Instance;
        if (player == null || col == null)
        {
            return;
        }
        Health health = player.GetComponent<Health>();
        Collider2D playerCol = player.GetComponent<Collider2D>();
        if (health == null || playerCol == null || health.IsDead || health.Current >= health.Max)
        {
            return;
        }
        if (col.bounds.Intersects(playerCol.bounds))
        {
            health.Heal(healAmount);
            PickedUp?.Invoke();
            Destroy(gameObject);
        }
    }

    // A glowing orb with physics, for when no pickup prefab is set
    public static HealthPickup CreateDefault(Vector2 position, float size, Color color)
    {
        GameObject orb = new GameObject("Health Orb");
        orb.transform.position = position;
        orb.transform.localScale = Vector3.one * size;

        SpriteRenderer sr = orb.AddComponent<SpriteRenderer>();
        sr.sprite = MagicCaster.GetOrbSprite();
        sr.color = color;
        sr.sortingOrder = 40;

        CircleCollider2D circle = orb.AddComponent<CircleCollider2D>();
        circle.radius = 0.35f;
        Rigidbody2D rb = orb.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.gravityScale = 6f;
        rb.linearDamping = 0.5f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        return orb.AddComponent<HealthPickup>();
    }
}
