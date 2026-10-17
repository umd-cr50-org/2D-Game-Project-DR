using UnityEngine;

// Test target for the player's hitboxes: logs each hit, flashes, and takes knockback if it has a Rigidbody2D.
// Put it on any object with a Collider2D.
public class TrainingDummy : MonoBehaviour, IHittable
{
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashTime = 0.1f;

    private SpriteRenderer sr;
    private Rigidbody2D rb;
    private Color normalColor;
    private float flashEndTime;
    private float totalDamage;

    void Start()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        if (sr != null)
        {
            normalColor = sr.color;
        }
    }

    void Update()
    {
        if (sr != null && Time.time >= flashEndTime)
        {
            sr.color = normalColor;
        }
    }

    public void OnHit(AttackHitInfo hit)
    {
        totalDamage += hit.damage;
        Debug.Log($"{name} hit by Attack{hit.attackNumber}{(hit.aerial ? " (air)" : "")}: {hit.damage} damage, {totalDamage} total");

        if (sr != null)
        {
            sr.color = flashColor;
            flashEndTime = Time.time + flashTime;
        }
        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = hit.knockback;
        }
    }
}
