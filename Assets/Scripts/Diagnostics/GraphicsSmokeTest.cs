#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core;
using Game.Player;
using Game.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Game.Diagnostics
{
    /// <summary>Opt-in rendered player benchmark. Never runs on an ordinary launch or writes player saves.</summary>

    public sealed class GraphicsSmokeTest : MonoBehaviour
    {
        private readonly List<string> errors = new List<string>();
        private readonly List<string> results = new List<string>();
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            bool requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-graphicsSmokeTest") >= 0;
#if UNITY_EDITOR
            requested |= UnityEditor.SessionState.GetBool("GraphicsSmoke", false);
            UnityEditor.SessionState.EraseBool("GraphicsSmoke");
#endif
            if (!requested)
            {
                return;
            }

            var root = new GameObject("Graphics validation");
            DontDestroyOnLoad(root);
            root.AddComponent<GraphicsSmokeTest>();
        }

        private void Awake()
        {
            output = Path.GetFullPath("Logs/GraphicsSmoke");
            Directory.CreateDirectory(output);
            GameSession.SuppressSave = true;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (Keyboard.current == null)
            {
                InputSystem.AddDevice<Keyboard>();
            }

            Application.logMessageReceived += Log;
            SceneManager.sceneLoaded += Scene;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
        }

        private void Scene(Scene scene, LoadSceneMode mode) => GameSession.SuppressSave = true;

        private void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors.Add(message);
            }
        }

        private void Start() => StartCoroutine(Guard(Run()));

        private IEnumerator Guard(IEnumerator routine)
        {
            while (true)
            {
                bool more;
                try
                {
                    more = routine.MoveNext();
                }
                catch (Exception exception)
                {
                    errors.Add(exception.ToString());
                    Finish();
                    yield break;
                }

                if (!more)
                {
                    Finish();
                    yield break;
                }

                yield return routine.Current;
            }
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(5f);
            Capture("01-menu");
            yield return new WaitForSecondsRealtime(0.5f);
            GameSession.SelectedLevel = 2;
            SceneManager.LoadScene(GameSession.CombatScene);
            yield return null;
            var pilot = FindObjectOfType<PlayerController>();
            Require(pilot != null, "Local pilot");
            pilot.captureCursor = false;
            Cursor.lockState = CursorLockMode.None;
            pilot.health.invulnerable = true;
            yield return new WaitForSecondsRealtime(10f);
            var view = pilot.GetComponent<FlightView>();
            Require(view != null, "Flight view");
            view.SetFirstPerson(false);
            pilot.weapon.TryFire();
            yield return new WaitForSecondsRealtime(0.04f);
            Capture("02-third-person-fire");
            yield return new WaitForSecondsRealtime(0.5f);

            // Use the real view-switch input before checking the rendered cockpit and camera ownership.
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.V));
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            Require(view.IsFirstPerson, "V key switches to first person");
            pilot.weapon.TryFire();
            yield return new WaitForSecondsRealtime(0.04f);
            Capture("03-cockpit");
            yield return new WaitForSecondsRealtime(0.5f);
            Require(FindObjectsOfType<AudioListener>().Length == 1, "One audio listener");
            var camera = Camera.main;
            Vector3 oldCamera = camera.transform.position;
            Vector3 oldPilot = pilot.transform.position;
            FloatingOrigin.Shift(new Vector3(-2400f, 50f, 100f));
            Require(Vector3.Distance(camera.transform.position - pilot.transform.position, oldCamera - oldPilot) < 0.01f, "Origin shift preserves camera-relative pose");

            yield return null; // Automatic rebase returns the pilot to the local origin.
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("04-after-origin-shift");
            yield return new WaitForSecondsRealtime(0.5f);
            var boss = FindObjectOfType<Game.Enemies.MotherShipBoss>();
            Require(boss != null, "Mothership present");
            var moveType = typeof(Game.Enemies.MotherShipBoss).GetNestedType("Move", BindingFlags.NonPublic);
            typeof(Game.Enemies.MotherShipBoss).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boss, new[] { Enum.Parse(moveType, "Minions") });
            yield return new WaitForSecondsRealtime(3f);
            Require(Vector3.Distance(boss.transform.position, pilot.transform.position) > 50f, "Escort phase clears the foreground");
            Capture("05-escort-phase");
            yield return new WaitForSecondsRealtime(2f);

            // Sample a fixed number of firing frames, then report median and upper-percentile timings.
            var frames = new List<float>();
            var gpuFrameTimes = new List<double>();
            double begin = Time.realtimeSinceStartupAsDouble;

            for (int sampleFrame = 0; sampleFrame < 720; sampleFrame++)
            {
                pilot.weapon.TryFire();
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                {
                    gpuFrameTimes.Add(timings[0].gpuFrameTime);
                }
            }

            double duration = Time.realtimeSinceStartupAsDouble - begin;
            frames.Sort();
            gpuFrameTimes.Sort();
            results.Add("Device=" + SystemInfo.graphicsDeviceName + " API=" + SystemInfo.graphicsDeviceType);
            results.Add("Resolution=" + Screen.width + "x" + Screen.height + " Upscaler=" + Graphics.GraphicsSettingsManager.Upscaler);
            results.Add("Samples=" + frames.Count + " FPS=" + (frames.Count / duration).ToString("F1") + " CPU/present p50ms=" + frames[frames.Count / 2].ToString("F2") + " p95ms=" + frames[(int)(frames.Count * 0.95f)].ToString("F2"));
            results.Add(gpuFrameTimes.Count > 0 ? "GPU p50ms=" + gpuFrameTimes[gpuFrameTimes.Count / 2].ToString("F2") + " p95ms=" + gpuFrameTimes[(int)(gpuFrameTimes.Count * 0.95f)].ToString("F2") : "GPU timing unavailable on this graphics backend");
            Require(errors.Count == 0, "No runtime errors");
        }

        private void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));

        private void Require(bool isValid, string text)
        {
            if (!isValid)
            {
                throw new InvalidOperationException(text);
            }

            results.Add("PASS " + text);
        }

        private void Finish()
        {
            foreach (string error in errors)
            {
                results.Add("FAIL " + error);
            }

            File.WriteAllLines(Path.Combine(output, "results.txt"), results);
            Debug.Log("GRAPHICS SMOKE " + (errors.Count == 0 ? "PASS" : "FAIL") + " " + string.Join(" | ", results));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
#else
            Application.Quit(errors.Count == 0 ? 0 : 1);
#endif
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            SceneManager.sceneLoaded -= Scene;
        }
    }
}
#endif
