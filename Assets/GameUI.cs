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

    [Header ("Boss Indicator (off-screen arrow)")]
    [Tooltip("Once the battle starts, an arrow at the screen edge points to the boss whenever it's off-screen.")]
    [SerializeField] private bool showBossIndicator = true;
    [SerializeField] private Color indicatorColor = new Color(1f, 0.25f, 0.2f);
    [Tooltip("Distance of the arrow from the screen edge (UI pixels).")]
    [SerializeField] private float indicatorEdgeMargin = 48f;

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
    private SpecialMeter bossMeter;
    private bool bossBarShown;

    private VisualElement playerHealthFill, playerHealthTrail, specialBar, specialFill;
    private Label playerHealthText, specialText;
    private VisualElement bossPanel, bossHealthFill, bossHealthTrail, phaseMarker, bossMeterBar, bossMeterFill;
    private VisualElement uiRoot, bossIndicator, bossIndicatorArrow;
    private Collider2D bossCollider;
    private VisualElement pauseMenu, pausePanel, controlsPanel, endScreen;
    private Label endTitle, endSubtitle;
    private Button resumeButton, retryButton, controlsBackButton;

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
        bossMeterBar = root.Q("boss-meter-bar");
        bossMeterFill = root.Q("boss-meter-fill");
        uiRoot = root;
        bossIndicator = root.Q("boss-indicator");
        bossIndicatorArrow = root.Q("boss-indicator-arrow");
        bossIndicatorArrow.style.backgroundImage = new StyleBackground(CreateArrowTexture());
        bossIndicatorArrow.style.unityBackgroundImageTintColor = indicatorColor;
        pauseMenu = root.Q("pause-menu");
        pausePanel = root.Q("pause-panel");
        controlsPanel = root.Q("controls-panel");
        endScreen = root.Q("end-screen");
        endTitle = root.Q<Label>("end-title");
        endSubtitle = root.Q<Label>("end-subtitle");

        resumeButton = root.Q<Button>("resume-button");
        retryButton = root.Q<Button>("retry-button");
        OnClick(resumeButton, Resume);
        controlsBackButton = root.Q<Button>("controls-back-button");
        OnClick(root.Q<Button>("controls-button"), () => ShowControls(true));
        OnClick(controlsBackButton, () => ShowControls(false));
        OnClick(root.Q<Button>("restart-button"), Restart);
        OnClick(retryButton, Restart);
        OnClick(root.Q<Button>("quit-button"), Quit);
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
        bossCollider = boss != null ? boss.GetComponent<Collider2D>() : null;
        bossMeter = boss != null ? boss.GetComponent<SpecialMeter>() : null;
        bossMeterBar.style.display = bossMeter != null ? DisplayStyle.Flex : DisplayStyle.None;
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
            if (paused && !controlsPanel.ClassListContains("hidden"))
            {
                ShowControls(false); // Esc on the controls screen goes back to the pause menu
            }
            else if (paused)
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
        UpdateBossIndicator();
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
            specialBar.EnableInClassList("charging", special.IsCharging);
            specialText.text = special.IsFull ? "ULTIMATE READY (V)"
                : special.IsCharging ? "CHARGING..."
                : $"MAGIC {Mathf.FloorToInt(special.Current)} / {Mathf.FloorToInt(special.Max)}";
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
        if (show && !bossBarShown)
        {
            bossBarShown = true;
            GameAudio.Play(GameAudio.UISound.BossBar);
        }
        bossPanel.EnableInClassList("faded", !show);
        bossPanel.EnableInClassList("phase2", boss.InPhase2);

        float fraction = bossHealth.Fraction;
        UpdateTrail(fraction, ref bossTrail, ref bossTrailWait);
        SetWidth(bossHealthFill, fraction);
        SetWidth(bossHealthTrail, bossTrail);
        if (bossMeter != null)
        {
            SetWidth(bossMeterFill, bossMeter.Fraction);
            bossMeterBar.EnableInClassList("full", bossMeter.IsFull);
        }
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

    // ---------- Boss indicator ----------

    // Off-screen: the arrow sits on the screen edge (inset by the margin) on the line from the screen center
    // to the boss, rotated to point at it. On-screen, before the battle, or once it's over: hidden.
    private void UpdateBossIndicator()
    {
        Camera cam = Camera.main;
        bool active = showBossIndicator && boss != null && cam != null && boss.BattleStarted
            && (bossHealth == null || !bossHealth.IsDead) && !gameOver;
        Vector3 bossPoint = bossCollider != null ? bossCollider.bounds.center : (boss != null ? boss.transform.position : Vector3.zero);
        Vector3 vp = active ? cam.WorldToViewportPoint(bossPoint) : Vector3.zero;
        bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

        float width = uiRoot.layout.width;
        float height = uiRoot.layout.height;
        if (!active || onScreen || float.IsNaN(width) || width <= 0f)
        {
            bossIndicator.AddToClassList("hidden");
            return;
        }
        bossIndicator.RemoveFromClassList("hidden");

        // Direction from the screen center to the boss, in UI pixels (UI y points down)
        if (vp.z < 0f)
        {
            vp = -vp; // behind the camera (not normally possible in 2D): flip so the arrow still makes sense
        }
        Vector2 center = new Vector2(width * 0.5f, height * 0.5f);
        Vector2 toBoss = new Vector2(vp.x * width, (1f - vp.y) * height) - center;
        if (toBoss.sqrMagnitude < 0.01f)
        {
            toBoss = Vector2.right;
        }

        // Slide out along that line until it meets the inset screen edge
        float halfW = Mathf.Max(center.x - indicatorEdgeMargin, 1f);
        float halfH = Mathf.Max(center.y - indicatorEdgeMargin, 1f);
        float scale = Mathf.Min(
            Mathf.Abs(toBoss.x) > 0.001f ? halfW / Mathf.Abs(toBoss.x) : float.MaxValue,
            Mathf.Abs(toBoss.y) > 0.001f ? halfH / Mathf.Abs(toBoss.y) : float.MaxValue);
        Vector2 pos = center + toBoss * scale;

        float size = bossIndicator.layout.width > 0f ? bossIndicator.layout.width : 64f;
        bossIndicator.style.left = pos.x - size * 0.5f;
        bossIndicator.style.top = pos.y - size * 0.5f;
        float angle = Mathf.Atan2(toBoss.y, toBoss.x) * Mathf.Rad2Deg; // clockwise from pointing right
        bossIndicatorArrow.style.rotate = new Rotate(angle);
        bossIndicator.style.opacity = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f); // gentle pulse
    }

    // A white arrowhead pointing right (tinted by Indicator Color), generated once
    private static Texture2D arrowTexture;
    private static Texture2D CreateArrowTexture()
    {
        if (arrowTexture != null)
        {
            return arrowTexture;
        }
        const int size = 64;
        arrowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Triangle: tip at the right middle, base along the left side (with a soft edge)
                float u = (x + 0.5f) / size;
                float v = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f; // 0 at the middle row, 1 at top/bottom
                float edge = (1f - u) - v; // > 0 inside the triangle
                float alpha = Mathf.Clamp01(edge * size * 0.5f);
                if (u < 0.45f)
                {
                    alpha = 0f; // trim the back so it reads as an arrowhead, not a wedge
                }
                arrowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        arrowTexture.Apply();
        return arrowTexture;
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
        GameAudio.Play(GameAudio.UISound.Pause);
        resumeButton.Focus();
    }

    private void Resume()
    {
        paused = false;
        Time.timeScale = 1f;
        pauseMenu.AddToClassList("hidden");
        ShowControls(false);
    }

    // The controls list replaces the pause buttons until Back (or Esc)
    private void ShowControls(bool show)
    {
        controlsPanel.EnableInClassList("hidden", !show);
        pausePanel.EnableInClassList("hidden", show);
        (show ? controlsBackButton : resumeButton).Focus();
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
            OnClick(button, () => LoadScene(mainMenuScene));
        }
        else
        {
            button.style.display = DisplayStyle.None; // no main menu scene in the build yet
        }
    }

    // Every button clicks before doing its job
    private void OnClick(Button button, System.Action action)
    {
        button.clicked += () =>
        {
            GameAudio.Play(GameAudio.UISound.ButtonClick);
            action();
        };
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
