using System;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Player;
using Game.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit, repeatable migration. Never runs automatically when teammates open Unity.
public static class GroupIntegrationSetup
{
    private const string ShipPath = "Assets/ArvisMag_Assets/BlockySpaceShips/Fihter/BlockyShip_Fihter_.prefab";
    private const string SkyPath = "Assets/ArvisMag_Assets/BlockySpaceShips/Fihter/SkyBox/SkyBox_Mat.mat";

    [MenuItem("Tools/Group Game/Configure integrated scenes")]
    public static void Configure()
    {
        ConfigureLayers();
        var enemy = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Enemy_Basic.prefab");
        try
        {
            enemy.transform.localScale = Vector3.one;
            ReplaceHull(enemy);
            if (enemy.GetComponent<global::Enemies>() == null)
            {
                enemy.AddComponent<global::Enemies>();
            }

            ConfigureMuzzle(enemy.GetComponentInChildren<Weapon>(), enemy.GetComponent<BoxCollider>());
            PrefabUtility.SaveAsPrefabAsset(enemy, "Assets/Prefabs/Enemy_Basic.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(enemy);
        }

        var combat = EditorSceneManager.OpenScene("Assets/Scenes/Combat_Test.unity");
        var pilot = UnityEngine.Object.FindObjectOfType<PlayerController>();
        ReplaceHull(pilot.gameObject);
        if (pilot.GetComponent<PlayerHealth>() == null)
        {
            pilot.gameObject.AddComponent<PlayerHealth>();
        }

        ConfigureMuzzle(pilot.weapon, pilot.GetComponent<BoxCollider>());
        var camera = Camera.main;
        var mount = pilot.aimPivot.Find("CameraMount");
        if (mount == null)
        {
            mount = new GameObject("CameraMount").transform;
            mount.SetParent(pilot.aimPivot, false);
        }

        mount.localPosition = new Vector3(0f, 1.3f, -7f);
        mount.localRotation = Quaternion.identity;
        camera.transform.SetParent(null, true);
        camera.transform.SetPositionAndRotation(mount.position, mount.rotation);
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 2000f;
        var follow = camera.GetComponent<MoveCamera>() ?? camera.gameObject.AddComponent<MoveCamera>();
        follow.cameraPostion = mount;
        follow.followRotation = true;
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        RenderSettings.skybox = sky;

        // This legacy sky's generated environment reflections wash out Standard materials in the player.
        RenderSettings.reflectionIntensity = 0f;
        UnityEngine.Object.FindObjectOfType<SpaceBackdrop>().skyboxMaterial = sky;
        EditorSceneManager.MarkSceneDirty(combat);
        EditorSceneManager.SaveScene(combat);
        var menu = EditorSceneManager.OpenScene("Assets/Space-Invaders-3D.unity");
        var document = UnityEngine.Object.FindObjectOfType<UIDocument>();
        if (document.GetComponent<MainMenuEvents>() == null)
        {
            document.gameObject.AddComponent<MainMenuEvents>();
        }

        RenderSettings.skybox = sky;
        RenderSettings.reflectionIntensity = 0f;
        Camera.main.clearFlags = CameraClearFlags.Skybox;

        // Display the original group ship in the entry scene as well as both combat roles.
        var display = GameObject.Find("GroupShipDisplay");
        if (display == null)
        {
            display = new GameObject("GroupShipDisplay");
        }

        ReplaceHull(display);
        UnityEngine.Object.DestroyImmediate(display.GetComponent<BoxCollider>());
        display.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 10f + Camera.main.transform.right * 5f;
        display.transform.rotation = Quaternion.Euler(0, 145, 0);
        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Space-Invaders-3D.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Combat_Test.unity", true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log("GROUP INTEGRATION: serialized menu, combat, group visuals, colliders, camera and build scenes.");
        IntegrationChecks.RunAdapters();
        IntegrationChecks.RunSceneChecks();
    }

    private static void ConfigureLayers()
    {
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tagList = tags.FindProperty("tags");
        bool enemyTag = false;

        for (int tagIndex = 0; tagIndex < tagList.arraySize; tagIndex++)
        {
            enemyTag |= tagList.GetArrayElementAtIndex(tagIndex).stringValue == "Enemy";
        }

        if (!enemyTag)
        {
            tagList.InsertArrayElementAtIndex(tagList.arraySize);
            tagList.GetArrayElementAtIndex(tagList.arraySize - 1).stringValue = "Enemy";
        }

        var layers = tags.FindProperty("layers");

        int[] indices = { 8, 9, 11, 12, 14 }; // Serialized prototype layers, including intentional gaps.

        // Only remove our own named slots; never replace a teammate's custom layer.
        for (int layerIndex = 8; layerIndex < layers.arraySize; layerIndex++)
        {
            if (Array.IndexOf(GameLayers.RequiredLayers, layers.GetArrayElementAtIndex(layerIndex).stringValue) >= 0)
            {
                layers.GetArrayElementAtIndex(layerIndex).stringValue = "";
            }
        }

        for (int requiredLayerIndex = 0; requiredLayerIndex < GameLayers.RequiredLayers.Length; requiredLayerIndex++)
        {
            var slot = layers.GetArrayElementAtIndex(indices[requiredLayerIndex]);
            if (slot.stringValue != "" && slot.stringValue != GameLayers.RequiredLayers[requiredLayerIndex])
            {
                throw new Exception("Layer migration conflict at " + indices[requiredLayerIndex]);
            }

            slot.stringValue = GameLayers.RequiredLayers[requiredLayerIndex];
        }

        tags.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ReplaceHull(GameObject root)
    {
        var childTransform = root.transform.Find("Hull");
        if (childTransform != null)
        {
            UnityEngine.Object.DestroyImmediate(childTransform.gameObject);
        }

        foreach (var component in root.GetComponents<MeshFilter>())
        {
            UnityEngine.Object.DestroyImmediate(component);
        }

        foreach (var component in root.GetComponents<MeshRenderer>())
        {
            UnityEngine.Object.DestroyImmediate(component);
        }

        foreach (var component in root.GetComponents<Collider>())
        {
            UnityEngine.Object.DestroyImmediate(component);
        }

        var hull = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ShipPath), root.transform);
        hull.name = "Hull";
        hull.transform.localPosition = Vector3.zero;
        hull.layer = root.layer;
        Bounds bounds = HullBounds(root.transform, hull.GetComponent<MeshFilter>());
        float span = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        hull.transform.localScale *= 4f / span;
        bounds = HullBounds(root.transform, hull.GetComponent<MeshFilter>());
        hull.transform.localPosition -= bounds.center;
        bounds = HullBounds(root.transform, hull.GetComponent<MeshFilter>());
        var collider = root.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = bounds.size;
        Debug.Log("GROUP HULL " + root.name + " bounds=" + bounds + " scale=" + hull.transform.localScale);
    }

    private static Bounds HullBounds(Transform root, MeshFilter filter)
    {
        var mesh = filter.sharedMesh.bounds;
        var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        var bounds = new Bounds(matrix.MultiplyPoint3x4(mesh.center), Vector3.zero);

        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            bounds.Encapsulate(matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents,
            new Vector3((cornerIndex & 1) == 0 ? -1 : 1, (cornerIndex & 2) == 0 ? -1 : 1, (cornerIndex & 4) == 0 ? -1 : 1))));
        }

        return bounds;
    }

    private static void ConfigureMuzzle(Weapon weapon, BoxCollider hull)
    {
        weapon.muzzle = weapon.transform;
        weapon.transform.localPosition = new Vector3(0f, 0f, hull.center.z + hull.size.z * 0.5f + 0.35f);
    }

    [MenuItem("Tools/Group Game/Build Windows")]
    public static void BuildWindows()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Space-Invaders-3D.unity", "Assets/Scenes/Combat_Test.unity" },
            locationPathName = "Builds/Windows/SpaceInvaders3D.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        var messages = new System.Collections.Generic.List<string>();

        foreach (var step in report.steps)
        {
            foreach (var message in step.messages)
            {
                messages.Add(step.name + " / " + message.type + " / " + message.content);
            }
        }

        System.IO.Directory.CreateDirectory("Logs");
        System.IO.File.WriteAllLines("Logs/build-report.txt", messages);
        Debug.Log("GROUP BUILD: " + report.summary.result + " errors=" + report.summary.totalErrors);
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
        {
            throw new Exception("Windows build reported errors; see Logs/build-report.txt");
        }
    }

    public static void ValidateAndBuild()
    {
        Validate();
        BuildWindows();
    }

    public static void Validate()
    {
        IntegrationChecks.RunAdapters();
        IntegrationChecks.RunSceneChecks();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SpaceLightingChecks.Run();
        NetworkRegressionChecks.Run();
        NetworkRegressionChecks.RunSnapshotTransport();
        NetworkRegressionChecks.RunSnapshotOrdering();
        NetworkRegressionChecks.RunCombatAuthority();
        NetBossPresentationChecks.Run();
    }
}
