using UnityEditor;
using UnityEditor.SceneManagement;
namespace Game.EditorTools
{
    // Runs graphics fixtures, then arms the opt-in rendered smoke test before entering play mode.
    public static class GraphicsPlayModeLauncher
    {
        public static void Run()
        {
            CockpitHudChecks.Run();
            OrbitalEnvironmentChecks.Run();
            GraphicsMaterialChecks.Run();
            EditorSceneManager.OpenScene("Assets/Space-Invaders-3D.unity");
            SessionState.SetBool("GraphicsSmoke", true);
            EditorApplication.isPlaying = true;
        }
    }
}
