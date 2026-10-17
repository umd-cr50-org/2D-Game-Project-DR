using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// The title screen (UI Toolkit, layout in Assets/UI/MainMenu.uxml): Start loads the game scene,
// Controls shows the key list, Quit closes the game. Lives in the MainMenu scene (made by Tools > Set Up Game UI).
[RequireComponent(typeof(UIDocument))]
public class MainMenu : MonoBehaviour
{
    [SerializeField] private string gameTitle = "2D GAME";
    [Tooltip("Scene loaded by Start (it must be in File > Build Profiles' scene list).")]
    [SerializeField] private string gameScene = "SampleScene";

    private VisualElement mainPanel, controlsPanel;

    void Start()
    {
        Time.timeScale = 1f;
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        mainPanel = root.Q("main-panel");
        controlsPanel = root.Q("controls-panel");
        root.Q<Label>("game-title").text = gameTitle;

        Button start = root.Q<Button>("start-button");
        start.clicked += () => SceneManager.LoadScene(gameScene);
        root.Q<Button>("controls-button").clicked += () => ShowControls(true);
        root.Q<Button>("back-button").clicked += () => ShowControls(false);
        root.Q<Button>("quit-button").clicked += Quit;
        start.Focus();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && controlsPanel.ClassListContains("hidden") == false)
        {
            ShowControls(false);
        }
    }

    private void ShowControls(bool show)
    {
        controlsPanel.EnableInClassList("hidden", !show);
        mainPanel.EnableInClassList("hidden", show);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
