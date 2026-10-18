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
    [SerializeField] private SoundEffect buttonClickSound = new SoundEffect();

    private VisualElement mainPanel, controlsPanel;

    void Start()
    {
        Time.timeScale = 1f;
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        mainPanel = root.Q("main-panel");
        controlsPanel = root.Q("controls-panel");
        root.Q<Label>("game-title").text = gameTitle;

        Button start = root.Q<Button>("start-button");
        OnClick(start, () => SceneManager.LoadScene(gameScene));
        OnClick(root.Q<Button>("controls-button"), () => ShowControls(true));
        OnClick(root.Q<Button>("back-button"), () => ShowControls(false));
        OnClick(root.Q<Button>("quit-button"), Quit);
        start.Focus();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && controlsPanel.ClassListContains("hidden") == false)
        {
            ShowControls(false);
        }
    }

    private void OnClick(Button button, System.Action action)
    {
        button.clicked += () =>
        {
            buttonClickSound.Play();
            action();
        };
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
