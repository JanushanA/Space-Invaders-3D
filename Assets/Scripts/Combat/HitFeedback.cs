using Game.Core;
using UnityEngine;
namespace Game.Combat
{
    // Flash on hit, and a small bar over enemies.
    // Shows visual hit feedback and an optional world-space health bar.
    public class HitFeedback : MonoBehaviour
    {
        public bool showWorldBar;
        private Health health;
        private Renderer[] renderers;
        private Color[] baseColours;
        private float flashUntil;
        private bool flashing;
        private Transform barRoot;
        private Transform barFill;

        private void Awake()
        {
            // Saves renderer colours once so a white damage flash can later restore the original ship material.
            health = GetComponent<Health>();
            renderers = GetComponentsInChildren<Renderer>();
            baseColours = new Color[renderers.Length];

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                // Handles child renderers because imported ships may be built from several separate meshes.
                if (renderers[rendererIndex].material != null)
                {
                    baseColours[rendererIndex] = renderers[rendererIndex].material.color;
                }
                else
                {
                    baseColours[rendererIndex] = Color.white;
                }
            }
        }

        private void Start()
        {
            // Delays bar creation until startup so a prefab can choose whether it needs world-space health UI.
            if (showWorldBar && barRoot == null)
            {
                BuildBar();
            }
        }

        public void EnsureWorldBar()
        {
            // Lets another system request a bar safely without creating a duplicate.
            showWorldBar = true;
            if (barRoot == null)
            {
                BuildBar();
            }
        }

        private void OnEnable()
        {
            // Subscribes to Health events while active so hits and health changes update visual feedback.
            if (health != null)
            {
                health.Damaged += HandleDamaged;
                health.HealthChanged += HandleHealthChanged;
            }
        }

        private void OnDisable()
        {
            // Removes event listeners when pooled objects are disabled, preventing duplicate callbacks next spawn.
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
                health.HealthChanged -= HandleHealthChanged;
            }
        }

        private void Update()
        {
            // Restores base colours once the short hit-flash timer has expired.
            if (!flashing || Time.time < flashUntil)
            {
                return;
            }

            flashing = false;
            RestoreColours();
        }

        private void LateUpdate()
        {
            // Rotates after camera movement so the health bar always faces the final camera direction this frame.
            if (barRoot == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                barRoot.rotation = Quaternion.LookRotation(barRoot.position - camera.transform.position, Vector3.up);
            }
        }

        private void HandleDamaged(Health source, DamageInfo info)
        {
            // Starts a short flash and changes every renderer to white to make a successful hit obvious.
            flashing = true;
            flashUntil = Time.time + 0.12f;

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex] != null && renderers[rendererIndex].material != null)
                {
                    renderers[rendererIndex].material.color = Color.white;
                }
            }
        }

        private void HandleHealthChanged(float current, float max)
        {
            // Converts health into a 0-to-1 fraction used to scale the green portion of the bar.
            if (barFill == null)
            {
                return;
            }

            float healthFraction = 0f;
            if (max > 0f)
            {
                healthFraction = Mathf.Clamp01(current / max);
            }

            barFill.localScale = new Vector3(healthFraction, 1f, 1f);
            barFill.localPosition = new Vector3((-0.5f + healthFraction * 0.5f), 0f, -0.01f);

            // Shifts the scaled fill so its left edge stays anchored while its right edge shrinks.
        }

        private void RestoreColours()
        {
            // Copies saved per-renderer colours back after the temporary damage flash ends.
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex] != null && renderers[rendererIndex].material != null)
                {
                    renderers[rendererIndex].material.color = baseColours[rendererIndex];
                }
            }
        }

        private void BuildBar()
        {
            // Measures mesh height and parent scale so the health bar sits above differently sized ship models.
            float parentScale = Mathf.Max(0.001f, transform.lossyScale.y);
            float height = 1.2f;
            BoxCollider hullCollider = GetComponent<BoxCollider>();
            if (hullCollider != null)
            {
                // Uses the top of the mesh bounds as the bar height for differently shaped imported ships.
                height = hullCollider.center.y + hullCollider.size.y * 0.5f + 0.8f / parentScale;
            }

            barRoot = new GameObject("HitBar").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = new Vector3(0f, height, 0f);
            barRoot.localScale = new Vector3(1.6f / parentScale, 0.18f / parentScale, 1f);

            // Divides by parent scale so the bar keeps a consistent visible size on scaled bosses and enemies.
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Quad);

            // Uses simple quads with removed colliders because the health bar is visual-only.
            back.name = "Back";
            back.transform.SetParent(barRoot, false);
            Destroy(back.GetComponent<Collider>());
            MeshRenderer backRenderer = back.GetComponent<MeshRenderer>();
            backRenderer.sharedMaterial = VisualUtil.MakeUnlit(new Color(0.15f, 0.05f, 0.05f, 1f));
            VisualUtil.HideShadows(backRenderer);
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(barRoot, false);
            Destroy(fill.GetComponent<Collider>());
            MeshRenderer fillRenderer = fill.GetComponent<MeshRenderer>();
            fillRenderer.sharedMaterial = VisualUtil.MakeUnlit(new Color(0.2f, 0.9f, 0.3f, 1f));
            VisualUtil.HideShadows(fillRenderer);
            barFill = fill.transform;
            if (health != null)
            {
                // Paints the bar immediately instead of waiting for the next damage or healing event.
                HandleHealthChanged(health.CurrentHealth, health.maxHealth);
            }
        }
    }
}
