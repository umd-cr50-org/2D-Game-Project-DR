using UnityEngine;

// Anything the player's attacks can hit (enemies, breakables, dummies) implements this.
// PlayerAttack finds it on the collider it overlaps or any of that collider's parents.
public interface IHittable
{
    void OnHit(AttackHitInfo hit);
}

public struct AttackHitInfo
{
    public int attackNumber; // 1-4
    public bool aerial; // thrown as part of the air string
    public float damage;
    public Vector2 knockback; // already pointed away from the player (x follows their facing)
    public GameObject attacker;
}
