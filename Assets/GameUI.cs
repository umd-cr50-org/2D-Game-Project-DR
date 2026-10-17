using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// The in-game UI (UI Toolkit). The layout is Assets/UI/GameUI.uxml and the look is Assets/UI/GameUI.uss;
// this script fills them in:
//   HUD        - player health (with a trail that shows the chunk just lost) and special meter, top left.
//                The boss bar fades in at the bottom once the battle starts; it turns purple in phase 2.
//   Pause menu - pauseKey (Esc) stops time: Resume / Restart / Main Menu / Quit.
//   End screen - after the player or the boss is defeated: Defeated or Victory, with Retry / Main Menu.
// Put it on the object with the UIDocument (Tools > Set Up Game UI does all of this).
[RequireComponent(typeof(UIDocument))]
public class GameUI : MonoBehaviour
{
    [Header ("Boss Bar")]
    [SerializeField] private string bossDisplayName = "Enemy Boss";

    [Header ("Bars")]
    [Tooltip("Health below this fraction turns the player's bar red.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthAt = 0.3f;
    [Tooltip("How long the light 'damage trail' waits before shrinking to the new health.")]
    [SerializeField] private float trailDelay = 0.4f;
    [Tooltip("How fast the trail shrinks (fraction of the bar per second).")]
    [SerializeField] private float trailSpeed = 0.8f;

    [Header ("Menus")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;
    [Tooltip("Scene loaded by the Main Menu buttons (it must be in File > Build Profiles' scene list).")]
    [SerializeField] private string mainMenuScene = "MainMenu";
    [Tooltip("Seconds between a defeat and the end screen appearing (so the defeat animation can play).")]
    [SerializeField] private float endScreenDelay = 1.5f;

    private Health playerHealth;
    private SpecialMeter special;
    private BossAI boss;
    private Health bossHealth;

    private VisualElement playerHealthFill, playerHealthTrail, specialBar, specialFill;
    private Label playerHealthText, specialText;
    private VisualElement bossPanel, bossHealthFill, bossHealthTrail, phaseMarker;
    private VisualElement pauseMenu, endScreen;
    private Label endTitle, endSubtitle;
    private Button resumeButton, retryButton;

    private float playerTrail = 1f, playerTrailWait;
    private float bossTrail = 1f, bossTrailWait;
    private bool paused;
    private bool gameOver;
    private float endScreenAt = -1f;

    void Start()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        playerHealthFill = root.Q("player-health-fill");
        playerHealthTrail = root.Q("player-health-trail");
        playerHealthText = root.Q<Label>("player-health-text");
        specialBar = root.Q("player-special-bar");
        specialFill = root.Q("player-special-fill");
        specialText = root.Q<Label>("player-special-text");
        bossPanel = root.Q("boss-panel");
        bossHealthFill = root.Q("boss-health-fill");
        bossHealthTrail = root.Q("boss-health-trail");
        phaseMarker = root.Q("boss-phase-marker");
        pauseMenu = root.Q("pause-menu");
        endScreen = root.Q("end-screen");
        endTitle = root.Q<Label>("end-title");
        endSubtitle = root.Q<Label>("end-subtitle");

        resumeButton = root.Q<Button>("resume-button");
        retryButton = root.Q<Button>("retry-button");
        resumeButton.clicked += Resume;
        root.Q<Button>("restart-button").clicked += Restart;
        retryButton.clicked += Restart;
        root.Q<Button>("quit-button").clicked += Quit;
        SetUpMainMenuButton(root.Q<Button>("pause-menu-button"));
        SetUpMainMenuButton(root.Q<Button>("end-menu-button"));

        // The player
        if (PlayerController.Instance != null)
        {
            playerHealth = PlayerController.Instance.GetComponent<Health>();
            special = PlayerController.Instance.GetComponent<SpecialMeter>();
        }
        if (playerHealth != null)
        {
            playerHealth.Died += _ => ShowEndScreenSoon();
        }
        else
        {
            Debug.LogWarning("GameUI: the player has no Health component, so its health bar stays full.");
        }
        specialBar.style.display = special != null ? DisplayStyle.Flex : DisplayStyle.None;

        // The boss
        boss = FindAnyObjectByType<BossAI>();
        bossHealth = boss != null ? boss.GetComponent<Health>() : null;
        root.Q<Label>("boss-name").text = bossDisplayName;
        if (bossHealth != null)
        {
            bossHealth.Died += _ => ShowEndScreenSoon();
            phaseMarker.style.left = Length.Percent(boss.Phase2At * 100f); // the line where phase 2 starts
        }
        else
        {
            bossPanel.style.display = DisplayStyle.None;
        }

        Time.timeScale = 1f;
    }

    void Update()
    {
        if (Input.GetKeyDown(pauseKey) && !gameOver)
        {
            if (paused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        if (endScreenAt >= 0f && Time.unscaledTime >= endScreenAt)
        {
            endScreenAt = -1f;
            ShowEndScreen();
        }

        UpdatePlayerBars();
        UpdateBossBar();
    }

    private void UpdatePlayerBars()
    {
        if (playerHealth != null)
        {
            float fraction = playerHealth.Fraction;
            UpdateTrail(fraction, ref playerTrail, ref playerTrailWait);
            SetWidth(playerHealthFill, fraction);
            SetWidth(playerHealthTrail, playerTrail);
            playerHealthFill.EnableInClassList("low", fraction <= lowHealthAt);
            playerHealthText.text = $"{Mathf.CeilToInt(playerHealth.Current)} / {Mathf.CeilToInt(playerHealth.Max)}";
        }
        if (special != null)
        {
            SetWidth(specialFill, special.Fraction);
            specialBar.EnableInClassList("full", special.IsFull);
            specialText.text = special.IsFull ? "SPECIAL READY" : "SPECIAL";
        }
    }

    private void UpdateBossBar()
    {
        if (bossHealth == null)
        {
            return;
        }
        // Fades in when the battle starts, fades out once the boss is beaten
        bool show = boss.BattleStarted && !bossHealth.IsDead;
        bossPanel.EnableInClassList("faded", !show);
        bossPanel.EnableInClassList("phase2", boss.InPhase2);

        float fraction = bossHealth.Fraction;
        UpdateTrail(fraction, ref bossTrail, ref bossTrailWait);
        SetWidth(bossHealthFill, fraction);
        SetWidth(bossHealthTrail, bossTrail);
    }

    // The trail jumps up on heals, and after a short wait slides down to meet the bar after damage
    private void UpdateTrail(float fraction, ref float trail, ref float wait)
    {
        if (fraction >= trail)
        {
            trail = fraction;
            wait = 0f;
            return;
        }
        wait += Time.deltaTime;
        if (wait >= trailDelay)
        {
            trail = Mathf.MoveTowards(trail, fraction, trailSpeed * Time.deltaTime);
        }
    }

    private static void SetWidth(VisualElement element, float fraction)
    {
        element.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
    }

    // ---------- Pause ----------

    private void Pause()
    {
        paused = true;
        Time.timeScale = 0f;
        pauseMenu.RemoveFromClassList("hidden");
        resumeButton.Focus();
    }

    private void Resume()
    {
        paused = false;
        Time.timeScale = 1f;
        pauseMenu.AddToClassList("hidden");
    }

    // ---------- End screen ----------

    private void ShowEndScreenSoon()
    {
        if (gameOver)
        {
            return;
        }
        gameOver = true;
        if (paused)
        {
            Resume();
        }
        endScreenAt = Time.unscaledTime + endScreenDelay;
    }

    private void ShowEndScreen()
    {
        bool won = playerHealth == null || !playerHealth.IsDead;
        endTitle.text = won ? "VICTORY" : "DEFEATED";
        endSubtitle.text = won ? $"{bossDisplayName} has fallen." : "Try again?";
        endScreen.EnableInClassList("victory", won);
        endScreen.EnableInClassList("defeat", !won);
        retryButton.text = won ? "Play Again" : "Retry";
        endScreen.RemoveFromClassList("hidden");
        retryButton.Focus();
    }

    // ---------- Buttons ----------

    private void SetUpMainMenuButton(Button button)
    {
        if (Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            button.clicked += () => LoadScene(mainMenuScene);
        }
        else
        {
            button.style.display = DisplayStyle.None; // no main menu scene in the build yet
        }
    }

    private void Restart()
    {
        LoadScene(SceneManager.GetActiveScene().name);
    }

    private void LoadScene(string sceneName)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy()
    {
        Time.timeScale = 1f; // never leave the game frozen when this scene unloads
    }
}
