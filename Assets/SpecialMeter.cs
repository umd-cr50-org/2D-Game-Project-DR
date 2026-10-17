using System;
using UnityEngine;

// The player's special meter: fills up as the player deals damage and as they take it.
// Shown on the HUD (GameUI). Spend() empties it - hook a special move (e.g. Player_Limit_Break) to that later.
[RequireComponent(typeof(Health))]
public class SpecialMeter : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxMeter = 100f;
    [Tooltip("Meter gained per point of damage this character deals (attacks and magic).")]
    [SerializeField] private float gainPerDamageDealt = 4f;
    [Tooltip("Meter gained per point of damage this character takes.")]
    [SerializeField] private float gainPerDamageTaken = 3f;

    public float Max => maxMeter;
    public float Current { get; private set; }
    public float Fraction => Current / maxMeter;
    public bool IsFull => Current >= maxMeter;

    public event Action<SpecialMeter> Changed;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        Health.AnyHit += OnAnyHit;
    }

    void OnDisable()
    {
        Health.AnyHit -= OnAnyHit;
    }

    private void OnAnyHit(Health victim, AttackHitInfo hit)
    {
        if (victim == health)
        {
            Add(hit.damage * gainPerDamageTaken);
        }
        else if (hit.attacker == gameObject)
        {
            Add(hit.damage * gainPerDamageDealt);
        }
    }

    public void Add(float amount)
    {
        float before = Current;
        Current = Mathf.Clamp(Current + amount, 0f, maxMeter);
        if (Current != before)
        {
            Changed?.Invoke(this);
        }
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
}
