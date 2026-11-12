using Game.Core;
using Game.Player;
using UnityEngine;
namespace Game.Combat
{
    // Collider, shadows, engine, hit flash. Runs on player and enemies.
    // Applies shared setup needed by runtime-created ship objects.
    public class ShipSetup : MonoBehaviour
    {
        private void Awake()
        {
            if (GetComponent<Game.Enemies.EnemyController>() != null && GetComponent<Game.Progression.SalvageDrop>() == null)
            {
                gameObject.AddComponent<Game.Progression.SalvageDrop>();
            }

            // Disables shadows and adds shared effects so runtime-created ships match the game's visual rules.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            {
                QualitySettings.shadows = ShadowQuality.Disable;
                VisualUtil.HideShadows(gameObject);
            }

            FitColliderToMesh();
            if (GetComponent<EngineTrail>() == null)
            {
                // Adds an engine trail only when the prefab has not already supplied one.
                gameObject.AddComponent<EngineTrail>();
            }

            if (GetComponent<HitFeedback>() == null)
            {
                // Gives enemies world bars while keeping the player health display in the main HUD.
                HitFeedback feedback = gameObject.AddComponent<HitFeedback>();
                feedback.showWorldBar = GetComponent<PlayerController>() == null;
            }
        }

        private void FitColliderToMesh()
        {
            // Replaces the old capsule with a box based on the imported mesh bounds for more accurate hits.
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                // Keeps the existing collider when this object has no mesh bounds to measure.
                return;
            }

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                Destroy(capsule);
            }

            BoxCollider boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                // Adds a box only when the prefab did not already contain one.
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            Bounds bounds = filter.sharedMesh.bounds;

            // Copies local mesh centre and size so the collider follows the visible ship shape.
            boxCollider.center = bounds.center;
            boxCollider.size = bounds.size;
        }
    }
}
