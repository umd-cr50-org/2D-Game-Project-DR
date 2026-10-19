using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Sets up the game's UI. Run it with the game scene open: Tools > Set Up Game UI (safe to run again).
//   - Player: adds Health (30) and a Special Meter.  Boss: adds Health (60, super armor: flashes but isn't stunned).
//     Characters that already have them keep their settings.
//   - Adds a "Game UI" object (UIDocument + GameUI) that shows Assets/UI/GameUI.uxml.
//   - Creates Assets/Scenes/MainMenu.unity (title screen) if it doesn't exist yet,
//     and puts MainMenu first and this scene second in the build's scene list.
public static class GameUISetup
{
    private const string GameUxmlPath = "Assets/UI/GameUI.uxml";
    private const string MenuUxmlPath = "Assets/UI/MainMenu.uxml";
    private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
    private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    [MenuItem("Tools/Set Up Game UI")]
    private static void Setup()
    {
        Scene gameScene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(gameScene.path))
        {
            EditorUtility.DisplayDialog("Set Up Game UI", "Save the game scene first, then run this again.", "OK");
            return;
        }

        VisualTreeAsset gameUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GameUxmlPath);
        VisualTreeAsset menuUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MenuUxmlPath);
        if (gameUxml == null || menuUxml == null)
        {
            Debug.LogError($"GameUISetup: missing {GameUxmlPath} or {MenuUxmlPath}.");
            return;
        }
        PanelSettings panel = GetPanelSettings();

        SetUpCharacters();
        SetUpGameUI(gameUxml, panel);
        EditorSceneManager.MarkSceneDirty(gameScene);

        CreateMainMenuScene(menuUxml, panel, gameScene.name);
        SetUpBuildScenes(gameScene.path);

        Debug.Log("GameUISetup: done. Save the scene (Ctrl+S). Edit the look in Assets/UI (UI Builder: Window > UI Toolkit > UI Builder).");
    }

    private static PanelSettings GetPanelSettings()
    {
        PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (panel != null)
        {
            return panel;
        }
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1200, 800);
        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        AssetDatabase.CreateAsset(panel, "Assets/UI/PanelSettings.asset");
        return panel;
    }

    private static void SetUpCharacters()
    {
        foreach (PlayerController character in Object.FindObjectsByType<PlayerController>())
        {
            bool isBoss = character.GetComponent<BossAI>() != null;
            GameObject go = character.gameObject;

            if (go.GetComponent<Health>() == null)
            {
                Health health = Undo.AddComponent<Health>(go);
                SerializedObject so = new SerializedObject(health);
                so.FindProperty("maxHealth").floatValue = isBoss ? 60f : 30f;
                so.FindProperty("hitStunTime").floatValue = isBoss ? 0f : 0.3f;
                so.FindProperty("invincibilityTime").floatValue = isBoss ? 0f : 0.6f;
                so.FindProperty("defeatAnimation").stringValue = isBoss ? "Boss_Lose" : "Player_Lose";
                so.ApplyModifiedProperties();
            }
            if (!isBoss && go.GetComponent<SpecialMeter>() == null)
            {
                Undo.AddComponent<SpecialMeter>(go);
            }
        }
    }

    private static void SetUpGameUI(VisualTreeAsset uxml, PanelSettings panel)
    {
        GameUI existing = Object.FindAnyObjectByType<GameUI>();
        GameObject go = existing != null ? existing.gameObject : new GameObject("Game UI");
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create Game UI");
        }
        UIDocument doc = go.GetComponent<UIDocument>();
        if (doc == null)
        {
            doc = Undo.AddComponent<UIDocument>(go);
        }
        Undo.RecordObject(doc, "Set Up Game UI");
        doc.panelSettings = panel;
        doc.visualTreeAsset = uxml;
        if (go.GetComponent<GameUI>() == null)
        {
            Undo.AddComponent<GameUI>(go);
        }
    }

    private static void CreateMainMenuScene(VisualTreeAsset uxml, PanelSettings panel, string gameSceneName)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) != null)
        {
            return; // already made: leave the user's version alone
        }

        Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        GameObject cam = new GameObject("Main Camera");
        Camera camera = cam.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        cam.tag = "MainCamera";
        SceneManager.MoveGameObjectToScene(cam, menu);

        GameObject ui = new GameObject("Main Menu UI");
        UIDocument doc = ui.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        doc.visualTreeAsset = uxml;
        MainMenu mainMenu = ui.AddComponent<MainMenu>();
        SerializedObject so = new SerializedObject(mainMenu);
        so.FindProperty("gameScene").stringValue = gameSceneName;
        so.ApplyModifiedProperties();
        SceneManager.MoveGameObjectToScene(ui, menu);

        EditorSceneManager.SaveScene(menu, MenuScenePath);
        EditorSceneManager.CloseScene(menu, true);
    }

    // Main menu first (it's what the built game opens on), then the game scene, then anything else already listed
    private static void SetUpBuildScenes(string gameScenePath)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
            .Where(s => s.path != MenuScenePath && s.path != gameScenePath)
            .ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(gameScenePath, true));
        scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
