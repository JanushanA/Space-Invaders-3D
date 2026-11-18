using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Game.EditorTools
{
    // Local editor integration: never enabled in a player, never changes scene/save data.
    [InitializeOnLoad]
    public static class ExpansionRequestRunner
    {
        static ExpansionRequestRunner()
        {
            EditorApplication.update += Poll;
        }
        static double next;

        static void Poll()
        {
            if (Application.isBatchMode || EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isPlaying)
            {
                return;
            }

            next = EditorApplication.timeSinceStartup + 1;
            const string request = "Logs/expansion-request.txt";
            if (!File.Exists(request))
            {
                return;
            }

            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (command == "checks")
                {
                    ExpansionChecks.Run();
                }
                else if (command == "build")
                {
                    GroupIntegrationSetup.BuildWindows();
                }
                else if (command == "refresh")
                {
                    AssetDatabase.Refresh();
                }
                else
                {
                    throw new Exception("Unknown local expansion request");
                }

                File.WriteAllText("Logs/expansion-result.txt", "PASS " + command);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                File.WriteAllText("Logs/expansion-result.txt", error.ToString());
            }
        }
    }
}
