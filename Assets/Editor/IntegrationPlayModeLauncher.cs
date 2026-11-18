using UnityEditor;
using UnityEditor.SceneManagement;

// Arms the solo smoke test for the next play-mode session starting at the menu scene.
public static class IntegrationPlayModeLauncher
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Space-Invaders-3D.unity");
        SessionState.SetBool("GroupSoloSmoke", true);
        EditorApplication.isPlaying = true;
    }
}
