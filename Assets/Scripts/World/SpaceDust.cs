using Game.Core;
using UnityEngine;
namespace Game.World
{
    // Nearby specks that stay in the world, so flying past them reads as speed.
    // Creates nearby white space particles that make forward movement easier to feel.
    public class SpaceDust : MonoBehaviour
    {
        public int count = 180;
        public float range = 45f;
        private ParticleSystem particles;

        private void Start()
        {
            // Creates a world-space particle field around the player to give motion a visible reference.
            GameObject dust = new GameObject("SpaceDust");
            dust.transform.SetParent(transform, false);
            particles = dust.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = 4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Keeps particles fixed in world space so flying past them makes the player feel like they are moving.
            main.maxParticles = count;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = count / 4f;

            // Replaces particles over their four-second lifetime, maintaining the requested average density.
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(range, range, range);

            // Spawns particles throughout a cube around the ship instead of from one visible point.
            ParticleSystem.ColorOverLifetimeModule tint = particles.colorOverLifetime;
            tint.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.85f),
                    new GradientAlphaKey(0f, 1f)
                });

            // Fades particles in and out so they do not pop sharply into the player's view.
            tint.color = gradient;
            ParticleSystemRenderer renderer = dust.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(Color.white);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;

            // Stretches particles along their movement so faster travel reads as short white space streaks.
            renderer.lengthScale = 3.5f;
            renderer.velocityScale = 0.08f;
            renderer.cameraVelocityScale = 0.35f;
            VisualUtil.HideShadows(renderer);
            ParticleSystem.Burst burst = new ParticleSystem.Burst(0f, (short)count);

            // Fills the field immediately rather than waiting for normal emission to build up after spawning.
            emission.SetBursts(new ParticleSystem.Burst[] { burst });
            particles.Play(true);
        }
    }
}
