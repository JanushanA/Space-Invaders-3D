using Game.Combat;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Positions the cockpit or chase camera and aligns firing with the view while keeping shots clear of the hull.
namespace Game.Player
{
    // Owns only the local pilot's existing camera. Remote avatars keep their renderers.
    [DefaultExecutionOrder(100)]
    public sealed class FlightView : MonoBehaviour
    {
        public static FlightView Instance
        {
            get; private set;
        }

        public bool IsFirstPerson
        {
            get; private set;
        }

        public Camera ViewCamera
        {
            get
            {
                return viewCamera;
            }
        }
        private PlayerController player;
        private Camera viewCamera;
        private MoveCamera follower;
        private bool followerWasEnabled;
        private float originalNearClip;
        private Renderer[] hullRenderers;
        private bool[] originalHidden;
        private MeshFilter[] hullMeshes;
        private static readonly Vector3 ChaseOffset = new Vector3(0f, 3.2f, -8f);
        private static readonly Vector3 CockpitOffset = new Vector3(0f, 0.4f, 0.6f);
        private const float AimDistance = 120f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void OnEnable()
        {
            player = GetComponent<PlayerController>();
            if (player == null || (Instance != null && Instance != this))
            {
                enabled = false;
                return;
            }

            Instance = this;
            Game.World.FloatingOrigin.Shifted += ShiftOrigin;
            Transform hull = transform.Find("Hull");
            hullRenderers = hull != null ? hull.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            hullMeshes = hull != null ? hull.GetComponentsInChildren<MeshFilter>(true) : new MeshFilter[0];
            originalHidden = new bool[hullRenderers.Length];

            for (int rendererIndex = 0; rendererIndex < hullRenderers.Length; rendererIndex++)
            {
                originalHidden[rendererIndex] = hullRenderers[rendererIndex].forceRenderingOff;
            }
        }

        private void Update()
        {
            if (!CanSwitch())
            {
                return;
            }

            if (TogglePressed())
            {
                SetFirstPerson(!IsFirstPerson);
            }
        }

        private void LateUpdate()
        {
            if (player == null || !player.isActiveAndEnabled || (player.health != null && !player.health.IsAlive))
            {
                SetMode(false);
                ReleaseCamera();
                return;
            }

            Camera current = Camera.main;
            if (current != viewCamera)
            {
                AcquireCamera(current);
            }

            ApplyCameraPose();
        }

        public void SetFirstPerson(bool firstPerson)
        {
            if (!CanSwitch())
            {
                return;
            }

            if (viewCamera == null)
            {
                AcquireCamera(Camera.main);
            }

            SetMode(firstPerson);
            ApplyCameraPose();
        }

        private bool CanSwitch()
        {
            return player != null && (!Application.isPlaying || player.isActiveAndEnabled) &&
                (player.health == null || player.health.IsAlive) && !PlayerSkills.MenuOpen && Time.timeScale > 0f;
        }

        private void SetMode(bool firstPerson)
        {
            bool changed = IsFirstPerson != firstPerson;
            IsFirstPerson = firstPerson;
            if (hullRenderers != null)
            {
                for (int rendererIndex = 0; rendererIndex < hullRenderers.Length; rendererIndex++)
                {
                    if (hullRenderers[rendererIndex] != null)
                    {
                        hullRenderers[rendererIndex].forceRenderingOff = firstPerson || originalHidden[rendererIndex];
                    }
                }
            }

            if (viewCamera != null)
            {
                viewCamera.nearClipPlane = firstPerson ? 0.05f : originalNearClip;
            }

            if (changed)
            {
                ResetCameraHistory();
            }
        }

        private void ShiftOrigin(Vector3 offset)
        {
            ResetCameraHistory();
        }

        private void ResetCameraHistory()
        {
            if (viewCamera != null && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)
            {
                HDCamera.GetOrCreate(viewCamera).Reset();
            }
        }

        private void AcquireCamera(Camera camera)
        {
            if (camera == viewCamera)
            {
                return;
            }

            ReleaseCamera();
            viewCamera = camera;
            if (viewCamera == null)
            {
                return;
            }

            originalNearClip = viewCamera.nearClipPlane;
            follower = viewCamera.GetComponent<MoveCamera>();
            if (follower != null)
            {
                followerWasEnabled = follower.enabled;
                follower.enabled = false;
            }

            SetMode(IsFirstPerson);
        }

        private void ReleaseCamera()
        {
            if (viewCamera != null)
            {
                viewCamera.nearClipPlane = originalNearClip;
            }

            if (follower != null)
            {
                follower.enabled = followerWasEnabled;
            }

            follower = null;
            viewCamera = null;
        }

        private Quaternion AimRotation
        {
            get
            {
                return player != null && player.aimPivot != null ? player.aimPivot.rotation : transform.rotation;
            }
        }

        private Vector3 CameraPosition(Quaternion rotation)
        {
            return transform.position + rotation * (IsFirstPerson ? CockpitOffset : ChaseOffset);
        }

        private void ApplyCameraPose()
        {
            if (viewCamera == null)
            {
                return;
            }

            Quaternion rotation = AimRotation;
            viewCamera.transform.SetPositionAndRotation(CameraPosition(rotation), rotation);
        }

        public bool OwnsWeapon(Weapon weapon)
        {
            return weapon != null && weapon.GetComponentInParent<PlayerController>() == player;
        }

        // Recalculate from the current input pose before firing, not last frame's LateUpdate camera.
        public void GetShotPose(Weapon weapon, out Vector3 position, out Vector3 direction)
        {
            Quaternion rotation = AimRotation;
            Quaternion inverse = Quaternion.Inverse(rotation);
            float topOffset = 0.35f;
            float front = 0.5f;
            if (hullMeshes != null)
            {
                foreach (MeshFilter filter in hullMeshes)
                {
                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }

                    Bounds bounds = filter.sharedMesh.bounds;

                    for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                    {
                        Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((cornerIndex & 1) == 0 ? -1 : 1, (cornerIndex & 2) == 0 ? -1 : 1, (cornerIndex & 4) == 0 ? -1 : 1));
                        Vector3 local = inverse * (filter.transform.TransformPoint(corner) - transform.position);
                        topOffset = Mathf.Max(topOffset, local.y);
                        front = Mathf.Max(front, local.z);
                    }
                }
            }

            Vector3 barrel = IsFirstPerson
                ? new Vector3(0.55f, 0.05f, Mathf.Max(2.5f, front + 0.35f))
                : new Vector3(0f, topOffset + 0.2f, front + 0.35f);
            position = transform.position + rotation * barrel;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 cameraPosition = CameraPosition(rotation);
            Vector3 target = cameraPosition + forward * AimDistance;
            LayerMask collisionLayers = weapon.hitMaskOverride.value != 0 ? weapon.hitMaskOverride : GameLayers.HitMaskFor(weapon.damageSource);
            RaycastHit hit;
            if (Physics.Raycast(cameraPosition, forward, out hit, AimDistance, collisionLayers, QueryTriggerInteraction.Ignore) &&
                Vector3.Dot(hit.point - position, forward) > 1f)
            {
                target = hit.point;
            }

            direction = (target - position).normalized;
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                return Keyboard.current.vKey.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER

            return Input.GetKeyDown(KeyCode.V);
#else
            return false;
#endif
        }

        private void OnDisable()
        {
            Game.World.FloatingOrigin.Shifted -= ShiftOrigin;
            SetMode(false);
            ReleaseCamera();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
