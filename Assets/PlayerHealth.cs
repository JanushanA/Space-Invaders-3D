using Game.Combat;
using Game.Core;
using UnityEngine;
using UnityEngine.Serialization;

// Jake's legacy API backed by the same Health used by HUD/combat/networking.
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [FormerlySerializedAs("health"), SerializeField] private float initialHealth = 50f;
    private Health state;

    public float health
    {
        get
        {
            return State.CurrentHealth;
        }
    }

    public bool IsAlive
    {
        get
        {
            return State.IsAlive;
        }
    }

    private Health State
    {
        get
        {
            if (state == null)
            {
                state = GetComponent<Health>();
                if (state == null)
                {
                    state = gameObject.AddComponent<Health>();
                    state.maxHealth = Mathf.Max(1f, initialHealth);
                    state.ResetHealth();
                }
            }

            return state;
        }
    }

    private void Awake()
    {
        state = State;
    }

    public void TakeDamage(float amount)
    {
        ApplyDamage(new DamageInfo(amount, DamageSource.Script, null, transform.position, Vector3.up));
    }

    public void ApplyDamage(DamageInfo info)
    {
        State.ApplyDamage(info);
    }
}
