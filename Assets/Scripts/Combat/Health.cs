using System;
using System.Collections;
using Game.Core;
using UnityEngine;
namespace Game.Combat
{
    // Stores health, damage, regeneration and death state for ships and enemies.
    public class Health : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public bool invulnerable;
        public bool deactivateOnDeath = true;
        public float deactivateDelay;
        public float incomingDamageMul = 1f;
        public float regenPerSecond;
        public float regenDelay = 3f;
        private float currentHealth;
        private float lastHurtTime;

        // Set before listeners run, so two hits in one frame kill once.
        private bool isDead;
        public event Action<Health, DamageInfo> Died;
        public event Action<Health, DamageInfo> Damaged;

        // For UI: current, max.
        public event Action<float, float> HealthChanged;

        public bool IsAlive
        {
            // Exposes the inverse death flag so callers can check combat state without changing it.
            get
            {
                return !isDead;
            }
        }

        public float CurrentHealth
        {
            // Exposes the current value for HUDs and network snapshots without allowing direct assignment.
            get
            {
                return currentHealth;
            }
        }

        // Initialises references and starting state before gameplay begins.
        private void Awake()
        {
            // Starts each enabled object at full health so pooled enemies can be reused safely.
            ResetHealth();
            RadarContact.Track(gameObject, RadarKind.Hostile);
        }

        // Applies one incoming hit after checking whether this ship can currently take damage.
        public void ApplyDamage(DamageInfo info)
        {
            // Applies multipliers here so all damage sources follow the same vulnerability rules.
            if ((Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost) ||
                isDead || invulnerable || info.Amount <= 0f || float.IsNaN(info.Amount) || float.IsInfinity(info.Amount))
            {
                return;
            }

            float amount = info.Amount * Mathf.Max(0f, incomingDamageMul);

            // Scales the incoming hit by the current damage multiplier before subtracting health.
            // This is how boss vulnerability and resistance affect every damage source consistently.
            if (amount <= 0f)
            {
                return;
            }

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            lastHurtTime = Time.time;

            // Stores the exact hit time so the regeneration delay starts again after every new hit.
            ReportHealth();
            if (Damaged != null)
            {
                // Sends hit details to visuals and UI before a fatal hit disables the object.
                Damaged(this, info);
            }

            if (currentHealth <= 0f)
            {
                // Calls the one death path only when clamped health has reached zero.
                Die(info);
            }
        }

        // Regenerates health only after the ship has avoided damage for the configured delay.
        private void Update()
        {
            // Delays regeneration after damage, preventing healing from cancelling out active combat.
            if ((Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost) || isDead || regenPerSecond <= 0f)
            {
                return;
            }

            if (currentHealth >= maxHealth)
            {
                // Skips unnecessary update work when health is already full.
                return;
            }

            if (Time.time < lastHurtTime + regenDelay)
            {
                // Waits until enough uninterrupted time has passed since the most recent damage.
                return;
            }

            Heal(regenPerSecond * Time.deltaTime);

            // Converts healing per second into this frame's small healing amount.
        }

        // Restores a positive amount of health and reports the change to connected UI.
        public void Heal(float amount)
        {
            // Adds healing but caps it at maximum health so repair effects cannot over-heal a ship.
            if ((Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost) || isDead || amount <= 0f)
            {
                return;
            }

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            ReportHealth();
        }

        // Increases the hull limit and grants the same amount as immediate current health.
        public void AddMaxHealth(float amount)
        {
            // Raises current health too, so buying a hull upgrade immediately gives its benefit.
            if (amount <= 0f)
            {
                return;
            }

            maxHealth += amount;
            currentHealth += amount;
            ReportHealth();
        }

        // Reused objects must call this or they respawn dead.
        public void ResetHealth()
        {
            // Clears the death flag and restores full health when a pooled or retried ship returns.
            isDead = false;
            currentHealth = maxHealth;
            ReportHealth();
        }

        // Updates this component's state through one shared method.
        public void SetNetworkHealth(float current)
        {
            // Applies host-reported health directly so clients display the authoritative combat state.
            currentHealth = Mathf.Clamp(current, 0f, maxHealth);
            ReportHealth();
            if (currentHealth > 0f)
            {
                // Revives the local display state when a fresh host snapshot reports positive health.
                isDead = false;
                return;
            }

            if (!isDead)
            {
                // Uses the normal death path once when the host snapshot reports zero hull.
                Die(new DamageInfo(0f, DamageSource.Script, null, transform.position, Vector3.up));
            }
        }

        private void ReportHealth()
        {
            // Broadcasts the current and maximum values so bars can update without reading private fields.
            if (HealthChanged != null)
            {
                HealthChanged(currentHealth, maxHealth);
            }
        }

        private void Die(DamageInfo killingBlow)
        {
            // Reports the death before deactivating the object so scoring and wave systems can react.
            isDead = true;
            currentHealth = 0f;
            if (Died != null)
            {
                // Notifies score, waves and multiplayer before this object is disabled or returned to a pool.
                Died(this, killingBlow);
            }

            if (!deactivateOnDeath)
            {
                // Leaves this object active when another system owns its death animation or cleanup.
                return;
            }

            if (deactivateDelay > 0f)
            {
                // Keeps the object active for a moment when its death visual needs time to play.
                StartCoroutine(DeactivateAfterDelay());
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // Waits for the configured death delay so explosions or feedback can finish before disabling the ship.
        private IEnumerator DeactivateAfterDelay()
        {
            // Waits in real game time before returning a killed object to its inactive state.
            yield return new WaitForSeconds(deactivateDelay);

            // May already be gone.
            if (this != null && gameObject != null)
            {
                gameObject.SetActive(false);
            }
        }

        // Keeps inspector-entered values safe while editing, preventing invalid health settings at runtime.
        private void OnValidate()
        {
            // Clamps editor values so a prefab cannot be saved with zero or negative maximum health.
            maxHealth = Mathf.Max(1f, maxHealth);
            if (!Application.isPlaying)
            {
                currentHealth = maxHealth;
            }
        }
    }
}
