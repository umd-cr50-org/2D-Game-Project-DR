using System.Collections.Generic;
using UnityEngine;

// A spell / projectile. MagicCaster spawns it and calls Launch; it then flies in a straight line,
// hits the first IHittable it touches (never the character who cast it), and disappears when it
// hits something solid (ground, walls) or when its lifetime runs out.
// It sweeps the space it moves through each frame, so fast shots can't pass through thin targets.
// No Rigidbody2D or collider needed on the prefab - just a sprite (or anything) and this script.
public class Projectile : MonoBehaviour
{
    [Tooltip("Size of the hit area (world units). MagicCaster sets this when it launches the projectile.")]
    [SerializeField] private float radius = 1f;
    [Tooltip("Layers the projectile can hit or be stopped by.")]
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField] private bool rotateToDirection = true;

    public SoundEffect ImpactSound { get; set; } // set by MagicCaster; plays when the projectile hits anything

    private GameObject owner;
    private Vector2 velocity;
    private float damage;
    private Vector2 knockback;
    private float dieAt;
    private bool launched;
    private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>();

    public void Launch(GameObject caster, Vector2 direction, float speed, float hitDamage, Vector2 hitKnockback, float lifetime, float hitRadius)
    {
        owner = caster;
        velocity = direction.normalized * speed;
        damage = hitDamage;
        // Knockback X points the way the projectile is travelling
        knockback = new Vector2(hitKnockback.x * Mathf.Sign(direction.x == 0f ? 1f : direction.x), hitKnockback.y);
        dieAt = Time.time + lifetime;
        radius = hitRadius;
        launched = true;
        if (rotateToDirection)
        {
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }

    void Update()
    {
        if (!launched)
        {
            return;
        }
        if (Time.time >= dieAt)
        {
            Destroy(gameObject);
            return;
        }

        Vector2 start = transform.position;
        Vector2 step = velocity * Time.deltaTime;
        float distance = step.magnitude;

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(hitLayers);
        filter.useTriggers = false;
        Physics2D.CircleCast(start, radius, step, filter, hits, distance);

        foreach (RaycastHit2D hit in hits)
        {
            if (owner != null && hit.collider.transform.IsChildOf(owner.transform))
            {
                continue; // never hits whoever cast it
            }
            if (hit.collider.GetComponent<HealthPickup>() != null)
            {
                continue; // flies through health orbs
            }

            IHittable target = hit.collider.GetComponentInParent<IHittable>();
            target?.OnHit(new AttackHitInfo
            {
                attackNumber = 0,
                damage = damage,
                knockback = knockback,
                attacker = owner,
            });
            // Hittable or solid scenery: either way the projectile is spent
            ImpactSound?.Play();
            transform.position = hit.centroid;
            Destroy(gameObject);
            return;
        }

        transform.position = start + step;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
