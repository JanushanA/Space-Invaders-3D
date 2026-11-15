using Game.Combat;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Net
{
    // The host sends these visual cues alongside each boss snapshot.
    public struct BossPresentationState
    {
        public byte Phase;
        public byte Stage;
        public bool Invulnerable;
        public bool BeamVisible;
        public bool BeamFiring;
        public bool BeamLocked;
        public bool HullTinted;
        public Vector3 BeamOrigin;
        public Vector3 BeamDirection;
        public Color HullColor;
    }

    // Renders a remote boss's warnings, tint, and health display without simulating its attacks.
    public class NetBossPresentation : MonoBehaviour
    {
        private LineRenderer beam;
        private Material beamMaterial;
        private MeshRenderer[] hull;
        private MaterialPropertyBlock[] originalProperties;
        private MaterialPropertyBlock tintProperties;
        private Text label;
        private Image fill;
        private Health health;
        private BossPresentationState state;

        public BossPresentationState Presentation => state;

        private void OnEnable() => Game.World.FloatingOrigin.Shifted += ShiftOrigin;

        private void OnDisable() => Game.World.FloatingOrigin.Shifted -= ShiftOrigin;

        private void ShiftOrigin(Vector3 delta) => state.BeamOrigin += delta;

        public void Apply(BossPresentationState next)
        {
            EnsureVisuals();
            state = next;
            beam.enabled = next.BeamVisible;
            beam.SetPosition(0, next.BeamOrigin);
            beam.SetPosition(1, next.BeamOrigin + next.BeamDirection.normalized * 90f);
            if (next.BeamFiring)
            {
                beam.startWidth = 2.4f;
                beam.endWidth = 2.8f;
                beam.startColor = new Color(1f, 0.12f, 0.08f, 1f);
                beam.endColor = new Color(1f, 0.45f, 0.15f, 0.85f);
            }
            else if (next.BeamLocked)
            {
                beam.startWidth = 0.9f;
                beam.endWidth = 1.2f;
                beam.startColor = new Color(1f, 1f, 0.85f, 1f);
                beam.endColor = new Color(1f, 0.25f, 0.1f, 0.95f);
            }
            else
            {
                beam.startWidth = 0.35f;
                beam.endWidth = 0.7f;
                beam.startColor = new Color(1f, 0.82f, 0.15f, 0.85f);
                beam.endColor = new Color(1f, 0.35f, 0.1f, 0.6f);
            }

            // Restore captured material overrides when the host removes its warning tint.
            for (int hullPartIndex = 0; hullPartIndex < hull.Length; hullPartIndex++)
            {
                if (hull[hullPartIndex] == null)
                {
                    continue;
                }

                if (next.HullTinted)
                {
                    hull[hullPartIndex].GetPropertyBlock(tintProperties);
                    tintProperties.SetColor("_Color", next.HullColor);
                    tintProperties.SetColor("_BaseColor", next.HullColor);
                    hull[hullPartIndex].SetPropertyBlock(tintProperties);
                }
                else
                {
                    hull[hullPartIndex].SetPropertyBlock(originalProperties[hullPartIndex].isEmpty ? null : originalProperties[hullPartIndex]);
                }
            }

            if (label != null)
            {
                label.text = "STAGE " + next.Stage + " / " + PhaseLabel(next) + (next.Invulnerable ? " / SHIELDED" : "");
            }

            if (fill != null)
            {
                fill.color = next.Stage == 2 ? new Color(1f, 0.42f, 0.08f) : UiChrome.NeonRed;
            }

            UpdateHealth();
        }

        private void LateUpdate()
        {
            UpdateHealth();
            if (beam != null && state.BeamVisible && !state.BeamFiring && !state.BeamLocked)
            {
                float pulse = 0.35f + Mathf.Abs(Mathf.Sin(Time.time * 10f)) * 0.65f;
                beam.startColor = new Color(1f, 0.82f, 0.15f, pulse);
                beam.endColor = new Color(1f, 0.35f, 0.1f, pulse * 0.7f);
            }
        }

        private void UpdateHealth()
        {
            if (fill != null && health != null)
            {
                fill.fillAmount = health.maxHealth > 0f ? Mathf.Clamp01(health.CurrentHealth / health.maxHealth) : 0f;
            }
        }

        private static string PhaseLabel(BossPresentationState value)
        {
            switch (value.Phase)
            {
                case 1:
                    return value.BeamLocked ? "LASER LOCK" : "CHARGING LASER";
                case 2:
                    return "LASER";
                case 3:
                case 4:
                    return "SWEEP LASER";
                case 5:
                    return "BARRAGE";
                case 6:
                    return value.Invulnerable ? "DESTROY ESCORTS" : "LAUNCHING ESCORTS";
                case 7:
                    return "NOVA";
                case 8:
                case 9:
                    return "RAMMING";
                case 10:
                    return "OVERLOAD - DOUBLE DAMAGE";
                case 11:
                    return "CORE OVERLOAD";
                default:
                    return "MOTHERSHIP";
            }
        }

        private void EnsureVisuals()
        {
            if (beam != null)
            {
                return;
            }

            health = GetComponent<Health>();

            // Preserve group textures/materials; warning tint uses renderer overrides only.
            hull = GetComponentsInChildren<MeshRenderer>(true);
            originalProperties = new MaterialPropertyBlock[hull.Length];
            tintProperties = new MaterialPropertyBlock();

            for (int hullPartIndex = 0; hullPartIndex < hull.Length; hullPartIndex++)
            {
                originalProperties[hullPartIndex] = new MaterialPropertyBlock();
                hull[hullPartIndex].GetPropertyBlock(originalProperties[hullPartIndex]);
            }

            var line = new GameObject("RemoteBossLaser");
            line.transform.SetParent(transform, false);
            beam = line.AddComponent<LineRenderer>();
            beam.enabled = false;
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.numCapVertices = 4;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beamMaterial = VisualUtil.MakeAdditive(Color.white);
            beam.sharedMaterial = beamMaterial;

            // The normal HUD already owns the boss bar; create a fallback only when it is absent.
            if (FindObjectOfType<Game.Player.GameHud>() != null)
            {
                return;
            }

            var sceneObject = new GameObject("RemoteBossHud");
            sceneObject.transform.SetParent(transform, false);
            var canvas = sceneObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = sceneObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var plate = UiChrome.MakeBox(sceneObject.transform, "BossBar", UiChrome.MetalDark);
            var rect = plate.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -18f);
            rect.sizeDelta = new Vector2(900f, 76f);
            label = UiChrome.MakeText(rect, "Label", "MOTHERSHIP", 22, TextAnchor.MiddleCenter, UiChrome.Gold);
            label.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            label.rectTransform.sizeDelta = new Vector2(880f, 34f);
            var well = UiChrome.MakeBox(rect, "Well", UiChrome.Ink);
            well.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            well.rectTransform.sizeDelta = new Vector2(780f, 22f);
            fill = UiChrome.MakeImage(well.transform, "Fill", UiChrome.Slant(), UiChrome.NeonRed);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            UiChrome.Stretch(fill.rectTransform);
        }

        private void OnDestroy()
        {
            if (beamMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(beamMaterial);
                }
                else
                {
                    DestroyImmediate(beamMaterial);
                }
            }
        }
    }
}
