using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Net;
using Game.World;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Game.Player
{
    // The player: input, look, movement and firing.
    // Transform based rather than physics, so nothing can nudge the ship off its aim.
    // Reads player input and turns it into ship movement, aiming, banking and firing.
    public class PlayerController : MonoBehaviour
    {
        public InputActionAsset actionAsset;
        public string actionMapName = "Player";
        public string moveActionName = "Move";
        public string lookActionName = "Look";
        public string fireActionName = "Fire";
        public string elevateActionName = "Elevate";
        public float moveSpeed = 12f;
        public float acceleration = 80f;
        public float verticalSpeed = 9f;

        // Child that pitches. The camera and muzzle hang off it.
        public Transform aimPivot;
        public float lookSensitivity = 0.12f;
        public float maxLookUp = 40f;
        public float maxLookDown = 55f;
        public bool invertVertical;
        public bool captureCursor = true;
        public Weapon weapon;
        public bool automaticFire = true;
        public Health health;

        // Legacy mouse axes are pre-scaled; this brings them back to pixels.
        private const float LegacyMouseToPixels = 10f;
        private InputActionMap actionMap;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction fireAction;
        private InputAction elevateAction;
        private PlayerSkills skills;
        private Vector3 currentVelocity;
        private float yaw;
        private float pitch;
        private float bankRoll;
        private bool inputEnabled = true;

        // False means the legacy Input Manager fallback is in use, which Unity needs
        // until the editor is restarted after switching backends.
        private static bool UsingInputSystem
        {
            get
            {
                return Keyboard.current != null;
            }
        }

        private void Awake()
        {
            // Uses the Health on this ship so movement can stop when the player dies.
            // This keeps damage and movement separate while still allowing them to communicate.
            if (health == null)
            {
                health = GetComponent<Health>();
            }

            if (health != null)
            {
                // Gives the player slow recovery after avoiding damage for a few seconds.
                // The Health component owns the actual timer so every healing source follows one rule.
                health.regenPerSecond = 0.45f;
                health.regenDelay = 3f;
            }

            if (weapon == null)
            {
                // Looks for a child weapon so the player prefab does not need every reference assigned by hand.
                weapon = GetComponentInChildren<Weapon>();
            }

            if (weapon == null)
            {
                Debug.LogWarning("[PlayerController] '" + name + "' has no Weapon, so it cannot shoot.", this);
            }

            if (GetComponent<ShipSetup>() == null)
            {
                // Adds required gameplay systems only when the prefab is missing them.
                // This lets a basic player ship become playable without changing its visual model.
                gameObject.AddComponent<ShipSetup>();
            }

            if (GetComponent<PlayerProgression>() == null)
            {
                gameObject.AddComponent<PlayerProgression>();
            }

            if (GetComponent<SkillTree>() == null)
            {
                gameObject.AddComponent<SkillTree>();
            }

            if (GetComponent<PlayerSkills>() == null)
            {
                gameObject.AddComponent<PlayerSkills>();
            }

            if (GetComponent<SkillMenu>() == null)
            {
                gameObject.AddComponent<SkillMenu>();
            }

            skills = GetComponent<PlayerSkills>();
            if (GetComponent<GameHud>() == null)
            {
                // Adds the player-only UI and effects after core combat systems are available.
                gameObject.AddComponent<GameHud>();
            }

            if (GetComponent<DeathFlow>() == null)
            {
                gameObject.AddComponent<DeathFlow>();
            }

            if (GetComponent<SpaceDust>() == null)
            {
                gameObject.AddComponent<SpaceDust>();
            }

            if (aimPivot == null)
            {
                Debug.LogError("[PlayerController] '" + name + "' has no aim pivot, so looking up and down will not work.", this);
            }

            if (GetComponent<PlayerHull>() == null)
            {
                gameObject.AddComponent<PlayerHull>();
            }

            ResolveActions();
            if (GetComponent<Game.Progression.PilotEquipment>() == null)
            {
                gameObject.AddComponent<Game.Progression.PilotEquipment>();
            }

            if (GetComponent<Game.Progression.HangarMenu>() == null)
            {
                gameObject.AddComponent<Game.Progression.HangarMenu>();
            }

            if (GetComponent<FlightView>() == null)
            {
                gameObject.AddComponent<FlightView>();
            }

            if (GetComponent<FloatingOrigin>() == null)
            {
                gameObject.AddComponent<FloatingOrigin>();
            }
        }

        private void OnEnable()
        {
            // Enables configured input and listens for death while this player object is active.
            // Removing both again in OnDisable prevents duplicate input and event callbacks.
            if (actionMap != null)
            {
                actionMap.Enable();
            }

            if (health != null)
            {
                health.Died += HandleDied;
            }

            PlayerRoster.Add(transform);
        }

        private void OnDisable()
        {
            if (actionMap != null)
            {
                actionMap.Disable();
            }

            SetCursorCaptured(false);
            if (health != null)
            {
                health.Died -= HandleDied;
            }

            PlayerRoster.Remove(transform);
        }

        private void Start()
        {
            Game.Graphics.GraphicsSettingsManager.PrepareHierarchy(gameObject);

            // Restores saved controls and starts a fresh combat score without resetting saved progression.
            invertVertical = GameSession.InvertLook;
            yaw = transform.eulerAngles.y;
            GameSession.BeginCombat();
            if (captureCursor)
            {
                SetCursorCaptured(true);
            }
        }

        private void Update()
        {
            // Applies look before movement so W always follows the ship's current aim direction.
            if (GameSession.ResetKeyPressed())
            {
                // Clears persistent progression only for the explicit reset key used during testing.
                PlayerSkills.MenuOpen = false;
                GameSession.ResetAllProgress();
                return;
            }

            if (PlayerSkills.MenuOpen)
            {
                // Pauses ship input while the skill menu is open so clicks cannot fire or move the ship.
                return;
            }

            if (health != null && !health.IsAlive)
            {
                // Stops control immediately after death; the death flow handles restart UI separately.
                return;
            }

            // Look first, because movement is relative to where the player faces.
            HandleCursor();
            Look();
            Move();
            if (skills != null && skills.BlockingFire)
            {
                // Prevents normal shots from overlapping skills such as the critical spray.
                return;
            }

            if (weapon != null && WantsToFire())
            {
                weapon.TryFire();
            }
        }

        private void Look()
        {
            // Stores horizontal and vertical view separately, allowing pitch to be clamped safely.
            // Skipped while the cursor is free, so the view does not spin in the editor.
            if (captureCursor && Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 look = LookInput;

            // Adds horizontal mouse movement to yaw so the whole ship turns around the vertical world axis.
            yaw += look.x * lookSensitivity;
            float verticalSign = -1f;
            if (invertVertical)
            {
                // Reverses only vertical aiming when the player chooses inverted controls.
                verticalSign = 1f;
            }

            pitch = global::PlayerCam.ClampPitch(pitch + look.y * lookSensitivity * verticalSign, maxLookUp, maxLookDown);

            // The clamp prevents the camera from flipping when the player looks too far vertically.
            ApplyBodyRotation();
        }

        private void Move()
        {
            // Converts WASD from local aim space into a world-space target velocity.
            Vector2 rawInput = MoveInput;

            // A/D banks the hull on Z. Facing and movement stay unbanked.
            float targetBank = -rawInput.x * 22f;

            // Smoothly approaches the visual roll target instead of snapping the hull while turning.
            bankRoll = Mathf.Lerp(bankRoll, targetBank, 1f - Mathf.Exp(-8f * Time.deltaTime));
            ApplyBodyRotation();
            Vector3 forward;
            Vector3 right;
            GetAimAxes(out forward, out right);
            Vector3 targetVelocity = global::PlayerMovement.ComputeFlightVelocity(
                rawInput, forward, right, moveSpeed, ElevateInput, verticalSpeed);
            currentVelocity = Vector3.MoveTowards(
                currentVelocity, targetVelocity, acceleration * Time.deltaTime);

            // Smooth acceleration makes thrust feel physical while keeping full control of the ship.
            Game.World.SweptMovement.Move(transform, currentVelocity * Time.deltaTime);

            // Multiplies velocity by delta time so movement distance is stable at different frame rates.
            KeepClearOfShips();
        }

        private void ApplyBodyRotation()
        {
            // Rolls the hull for visual banking, while the opposite pivot roll keeps aim and camera level.
            transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, bankRoll);
            if (aimPivot != null)
            {
                // Opposite Z so the camera and gun stay level while the hull banks.
                aimPivot.localRotation = Quaternion.Euler(pitch, 0f, -bankRoll);
            }
        }

        private void GetAimAxes(out Vector3 forward, out Vector3 right)
        {
            // Recreates the complete aiming rotation and extracts its forward and right movement axes.
            Quaternion aimRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
            forward = aimRotation * Vector3.forward;
            right = aimRotation * Vector3.right;
        }

        public void GetAimPose(out float outYaw, out float outPitch, out float outBank)
        {
            outYaw = yaw;
            outPitch = pitch;
            outBank = bankRoll;
        }

        public void DashAlongAim(float distance)
        {
            // Moves along the current aim, allowing dash to work while aiming up or down.
            Vector3 direction;
            Vector3 right;
            GetAimAxes(out direction, out right);
            Game.World.SweptMovement.Move(transform, direction * distance);
            currentVelocity = direction * (moveSpeed * 1.5f);
            KeepClearOfShips();
        }

        // Cylinder on the playfield so hulls do not occupy the same XZ.
        private void KeepClearOfShips()
        {
            EnemyController[] fighters = FindObjectsOfType<EnemyController>();

            // Checks each fighter because separation is calculated between the player and one hull at a time.
            int shipIndex;

            for (shipIndex = 0; shipIndex < fighters.Length; shipIndex++)
            {
                if (fighters[shipIndex] == null || !fighters[shipIndex].isActiveAndEnabled)
                {
                    continue;
                }

                float range = fighters[shipIndex].minPlanarRange;
                ShipClearance.PushAway(
                    transform,
                    fighters[shipIndex].transform,
                    range,
                    ShipClearance.FighterHeight,
                    ref currentVelocity);
            }

            MotherShipBoss[] bosses = FindObjectsOfType<MotherShipBoss>();

            // Uses a larger clearance cylinder for bosses because their models are much wider than fighters.
            for (shipIndex = 0; shipIndex < bosses.Length; shipIndex++)
            {
                if (bosses[shipIndex] == null || !bosses[shipIndex].isActiveAndEnabled)
                {
                    continue;
                }

                ShipClearance.PushAway(
                    transform,
                    bosses[shipIndex].transform,
                    bosses[shipIndex].PlanarRange,
                    ShipClearance.BossHeight,
                    ref currentVelocity);
            }
        }

        private bool WantsToFire()
        {
            // Supports hold-to-fire and click-to-fire by choosing the input mode set for this player.
            if (automaticFire)
            {
                return FireHeld;
            }

            return FirePressedThisFrame;
        }

        private void HandleDied(Health source, DamageInfo killingBlow)
        {
            // Removes momentum and blocks new input so the dead ship cannot keep drifting or shooting.
            currentVelocity = Vector3.zero;
            inputEnabled = false;
        }

        private void HandleCursor()
        {
            // Lets Escape release the cursor and a click capture it again for mouse-look controls.
            if (!captureCursor)
            {
                return;
            }

            if (CancelPressedThisFrame)
            {
                SetCursorCaptured(false);
            }
            else if (Cursor.lockState != CursorLockMode.Locked && PointerPressedThisFrame)
            {
                SetCursorCaptured(true);
            }
        }

        private static void SetCursorCaptured(bool captured)
        {
            // Changes both lock mode and visibility together so the cursor state never feels inconsistent.
            if (captured)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
            }

            Cursor.visible = !captured;
        }

        private void ResolveActions()
        {
            // Connects named actions from the Input Action Asset to this controller once at startup.
            if (actionAsset == null)
            {
                Debug.LogWarning("[PlayerController] '" + name + "' has no Input Action Asset. Using the legacy Input Manager.", this);
                return;
            }

            actionMap = actionAsset.FindActionMap(actionMapName, false);
            if (actionMap == null)
            {
                Debug.LogError("[PlayerController] '" + actionAsset.name + "' has no action map '" + actionMapName + "'.", this);
                return;
            }

            moveAction = FindAction(moveActionName);
            lookAction = FindAction(lookActionName);
            fireAction = FindAction(fireActionName);
            elevateAction = FindAction(elevateActionName);
        }

        private InputAction FindAction(string actionName)
        {
            // Reports a missing input action early, making an input setup error easier to diagnose.
            InputAction action = actionMap.FindAction(actionName);
            if (action == null)
            {
                Debug.LogError("[PlayerController] '" + actionMapName + "' has no action '" + actionName + "'.", this);
            }

            return action;
        }

        private Vector2 MoveInput
        {
            get
            {
                if (!inputEnabled)
                {
                    // Returns no movement after death or while another UI system has taken control.
                    return Vector2.zero;
                }

                if (UsingInputSystem && moveAction != null)
                {
                    // Prefers the new Input System but preserves the legacy fallback for existing project setups.
                    return moveAction.ReadValue<Vector2>();
                }

                return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            }
        }

        // Pixels, already frame independent. Do not scale by delta time.
        private Vector2 LookInput
        {
            get
            {
                if (!inputEnabled)
                {
                    return Vector2.zero;
                }

                if (UsingInputSystem && lookAction != null)
                {
                    return lookAction.ReadValue<Vector2>();
                }

                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * LegacyMouseToPixels;
            }
        }

        // Space up, Left Ctrl down.
        private float ElevateInput
        {
            get
            {
                if (!inputEnabled)
                {
                    return 0f;
                }

                if (UsingInputSystem && elevateAction != null)
                {
                    return elevateAction.ReadValue<float>();
                }

                float verticalInput = 0f;

                // Builds a signed vertical value so Space is positive and Ctrl is negative.
                if (Input.GetKey(KeyCode.Space))
                {
                    verticalInput = 1f;
                }

                float down = 0f;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    down = 1f;
                }

                return verticalInput - down;
            }
        }

        private bool FireHeld
        {
            get
            {
                if (!inputEnabled)
                {
                    return false;
                }

                if (UsingInputSystem && fireAction != null)
                {
                    return fireAction.IsPressed();
                }

                return Input.GetMouseButton(0);
            }
        }

        private bool FirePressedThisFrame
        {
            get
            {
                if (!inputEnabled)
                {
                    return false;
                }

                if (UsingInputSystem && fireAction != null)
                {
                    return fireAction.WasPressedThisFrame();
                }

                return Input.GetMouseButtonDown(0);
            }
        }

        // Ignores death, so the mouse can always be freed.
        private static bool CancelPressedThisFrame
        {
            get
            {
                if (UsingInputSystem)
                {
                    return Keyboard.current.escapeKey.wasPressedThisFrame;
                }

                return Input.GetKeyDown(KeyCode.Escape);
            }
        }

        private static bool PointerPressedThisFrame
        {
            get
            {
                if (UsingInputSystem)
                {
                    if (Mouse.current == null)
                    {
                        return false;
                    }

                    return Mouse.current.leftButton.wasPressedThisFrame;
                }

                return Input.GetMouseButtonDown(0);
            }
        }
    }
}
