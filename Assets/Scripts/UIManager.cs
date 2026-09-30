using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 時間與血量：教學面板、掃描提示、遊戲中 HUD（倒數、愛心、擊退數）、提示字、受擊閃紅、勝負結算。
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    // 執行時才組出來的文字。場景設定腳本會先把這些字烘進字型，測試也會檢查不缺字。
    public const string StartToastFormat = "守住神碑 {0} 秒！";
    public const string StartToastSub = "點一下鎖定・再點一下擊退";
    public const string WarningToastFormat = "最後 {0} 秒！";
    public const string ScanSearching = "慢慢移動手機，掃描周圍的地面";
    public const string ScanFound = "找到地面了，神碑即將降臨…";
    public const string NewEnemyToastFormat = "新的萬年龜：{0}";
    public const string SideToast = "萬年龜開始從兩側包抄！";
    public const string SideToastSub = "留意畫面邊緣的箭頭";
    public static readonly string[] RuntimeTextSamples =
    {
        string.Format(StartToastFormat, 60), StartToastSub, string.Format(WarningToastFormat, 10),
        ScanSearching, ScanFound, "擊退 0123456789", "擊退萬年龜　隻 神碑耐久　/ 守護時間　:",
        string.Format(NewEnemyToastFormat, ""), NonARFallback.NoticeText, SideToast, SideToastSub,
    };

    const string Highlight = "#F2C45A";

    [Header("計時")]
    public TextMeshProUGUI timerText;
    [SerializeField] private float totalTime = 60f;
    [Tooltip("剩下幾秒時計時器轉紅並跳動提醒")]
    [SerializeField] private float warningTime = 10f;
    [SerializeField] private Color warningColor = new Color(1f, 0.25f, 0.2f);

    [Header("面板")]
    [Tooltip("教學 1：故事")]
    public GameObject firstPanel;
    [Tooltip("教學 2：遊戲操作")]
    public GameObject secondPanel;
    [Tooltip("教學 3：掃描地面尋找神碑")]
    public GameObject thirdPanel;
    [Tooltip("遊戲中 HUD")]
    public GameObject startPanel;
    public GameObject winPanel;
    public GameObject losePanel;
    [Tooltip("教學面板底部的「點擊畫面繼續」提示")]
    public TextMeshProUGUI tapHint;

    [Header("掃描提示")]
    public TextMeshProUGUI scanStatusText;
    [Tooltip("畫面中央的掃描準星（脈動）")]
    public RectTransform scanReticle;

    [Header("模擬場景提示（不支援 AR 的手機）")]
    public TextMeshProUGUI fallbackNotice;

    [Header("提示字")]
    public CanvasGroup toast;
    public TextMeshProUGUI toastText;

    [Header("血量")]
    public Image[] healthImages;      // UI 上的愛心圖示

    [Header("戰績")]
    public TextMeshProUGUI killText;
    public TextMeshProUGUI winStatsText;
    public TextMeshProUGUI loseStatsText;

    [Header("受擊回饋")]
    public Image damageFlash;
    [SerializeField] private float flashAlpha = 0.6f;
    [SerializeField] private float flashDuration = 0.4f;

    public int CurrentHealth { get; private set; }
    public int KillCount { get; private set; }
    public int MaxHealth => healthImages != null ? healthImages.Length : 0;
    public bool IntroFinished { get; private set; }
    public float TotalTime => totalTime;
    public float RemainingTime => countdownTime;
    /// <summary>0 = 剛開局，1 = 倒數結束</summary>
    public float Progress01 => totalTime > 0f ? Mathf.Clamp01(1f - countdownTime / totalTime) : 1f;

    private float countdownTime;
    private bool isCounting;
    private int introStep;
    private int lastWholeSecond;
    private bool warnedThisRound;
    private Color timerBaseColor = Color.white;
    private Coroutine flashRoutine;
    private Coroutine toastRoutine;
    private Vector3[] heartScales;
    private Color[] heartColors;
    private int heartAnimVersion;     // 重置血量時 +1，讓進行中的愛心動畫自行結束

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Start()
    {
        if (timerText != null)
            timerBaseColor = timerText.color;

        CacheHearts();
        countdownTime = totalTime;
        UpdateTimerUI(countdownTime);
        ResetHealth();          // 一開始補滿血
        SetKills(0);
        SetFlashAlpha(0f);
        HideToast();
        SetScanStatus(false);
        if (fallbackNotice != null && !NonARFallback.Active)
            SetActive(fallbackNotice.gameObject, false);

        SetActive(startPanel, false);
        SetActive(winPanel, false);
        SetActive(losePanel, false);
        ShowIntroStep(0);
    }

    private void Update()
    {
        // 「點擊畫面繼續」緩慢閃爍
        if (tapHint != null && tapHint.gameObject.activeInHierarchy)
            tapHint.alpha = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * 2.2f));

        // 掃描準星脈動
        if (scanReticle != null && scanReticle.gameObject.activeInHierarchy)
        {
            float k = Mathf.Repeat(Time.time * 0.8f, 1f);
            scanReticle.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.25f, k);
            Graphic g = scanReticle.GetComponent<Graphic>();
            if (g != null)
                g.canvasRenderer.SetAlpha(1f - k);
        }

        if (!isCounting)
            return;

        countdownTime -= Time.deltaTime;
        if (countdownTime <= 0f)
        {
            countdownTime = 0f;
            UpdateTimerUI(countdownTime);
            isCounting = false;
            TimerEnd();
            return;
        }

        UpdateTimerUI(countdownTime);
        CheckCountdownCues();
    }

    // ------------------- 教學 -------------------

    /// <summary>點畫面進下一頁（教學面板上的全螢幕按鈕呼叫）</summary>
    public void NextIntro()
    {
        if (IntroFinished)
            return;

        GameAudio.Play(GameAudio.Sfx.Ui);
        ShowIntroStep(introStep + 1);
    }

    private void ShowIntroStep(int step)
    {
        introStep = step;
        SetActive(firstPanel, step == 0);
        SetActive(secondPanel, step == 1);
        SetActive(thirdPanel, step >= 2);
        if (tapHint != null)
            SetActive(tapHint.gameObject, step < 2);

        if (step >= 2)
        {
            IntroFinished = true;

            // 教學看完才放神碑；平面可能在看教學時就已經掃到了
            if (SinglePlacementManager.instance != null)
                SinglePlacementManager.instance.TryPlace();
        }
    }

    /// <summary>回到「尋找神碑」提示（重新放置神碑時使用）</summary>
    public void ShowScanPrompt()
    {
        isCounting = false;
        SetActive(startPanel, false);
        SetActive(winPanel, false);
        SetActive(losePanel, false);
        SetActive(thirdPanel, true);
        SetScanStatus(false);
    }

    /// <summary>不支援 AR、改用模擬場景時，在畫面底部常駐一行說明</summary>
    public void ShowFallbackNotice(string text)
    {
        if (fallbackNotice == null)
            return;
        fallbackNotice.text = text;
        SetActive(fallbackNotice.gameObject, true);
    }

    /// <summary>掃描提示：還在找地面／已找到、神碑即將出現</summary>
    public void SetScanStatus(bool planeFound)
    {
        if (scanStatusText != null)
            scanStatusText.text = planeFound ? ScanFound : ScanSearching;
        if (scanReticle != null)
            SetActive(scanReticle.gameObject, !planeFound);
    }

    // ------------------- 遊戲流程 -------------------

    public void StartGame()
    {
        SetActive(firstPanel, false);
        SetActive(secondPanel, false);
        SetActive(thirdPanel, false);
        SetActive(winPanel, false);
        SetActive(losePanel, false);
        SetActive(startPanel, true);

        countdownTime = totalTime;
        lastWholeSecond = Mathf.CeilToInt(totalTime);
        warnedThisRound = false;
        isCounting = true;
        UpdateTimerUI(countdownTime);

        ResetHealth();      // 開始遊戲時重置血量顯示
        SetKills(0);
        StopFlash();
        GameAudio.DuckBgm(false);

        ShowToast($"{string.Format(StartToastFormat, Mathf.RoundToInt(totalTime))}\n<size=58%>{StartToastSub}</size>", 2.4f);
    }

    // 最後幾秒：提示字＋每秒一聲
    private void CheckCountdownCues()
    {
        int whole = Mathf.CeilToInt(countdownTime);
        if (whole == lastWholeSecond)
            return;

        lastWholeSecond = whole;
        if (whole > warningTime)
            return;

        if (!warnedThisRound)
        {
            warnedThisRound = true;
            ShowToast(string.Format(WarningToastFormat, whole), 1.4f);
        }
        GameAudio.Play(GameAudio.Sfx.Tick);
    }

    private void TimerEnd()
    {
        // 撐過倒數＝勝利
        if (SinglePlacementManager.instance != null)
            SinglePlacementManager.instance.EndGame(true);
        else
            ShowResult(true);
    }

    public void ShowResult(bool won)
    {
        isCounting = false;
        HideToast();
        SetActive(startPanel, false);
        SetActive(winPanel, won);
        SetActive(losePanel, !won);

        TextMeshProUGUI stats = won ? winStatsText : loseStatsText;
        if (stats != null)
            stats.text = BuildStats();

        GameAudio.Play(won ? GameAudio.Sfx.Win : GameAudio.Sfx.Lose);
        GameAudio.DuckBgm(true);
    }

    private string BuildStats()
    {
        int survived = Mathf.FloorToInt(totalTime - countdownTime);
        return $"擊退萬年龜　<color={Highlight}>{KillCount}</color> 隻\n" +
               $"神碑耐久　<color={Highlight}>{CurrentHealth} / {MaxHealth}</color>\n" +
               $"守護時間　<color={Highlight}>{survived / 60:00}:{survived % 60:00}</color>";
    }

    private void UpdateTimerUI(float time)
    {
        if (timerText == null)
            return;

        int seconds = Mathf.CeilToInt(time);
        timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";

        // 最後幾秒轉紅，每跳一秒放大一下
        bool warn = isCounting && time <= warningTime;
        timerText.color = warn ? warningColor : timerBaseColor;
        float punch = warn ? 1f + 0.2f * (time % 1f) : 1f;
        timerText.rectTransform.localScale = Vector3.one * punch;
    }

    // ------------------- 提示字 -------------------

    public void ShowToast(string message, float hold)
    {
        if (toast == null || toastText == null)
            return;

        if (toastRoutine != null)
            StopCoroutine(toastRoutine);
        toastText.text = message;
        toastRoutine = StartCoroutine(ToastRoutine(hold));
    }

    private void HideToast()
    {
        if (toastRoutine != null)
        {
            StopCoroutine(toastRoutine);
            toastRoutine = null;
        }
        if (toast != null)
        {
            toast.alpha = 0f;
            SetActive(toast.gameObject, false);
        }
    }

    private IEnumerator ToastRoutine(float hold)
    {
        SetActive(toast.gameObject, true);
        Transform t = toast.transform;

        // 彈出
        for (float e = 0f; e < 0.25f; e += Time.deltaTime)
        {
            float k = e / 0.25f;
            toast.alpha = k;
            t.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, 1f - (1f - k) * (1f - k));
            yield return null;
        }
        toast.alpha = 1f;
        t.localScale = Vector3.one;

        yield return new WaitForSeconds(hold);

        // 淡出
        for (float e = 0f; e < 0.4f; e += Time.deltaTime)
        {
            toast.alpha = 1f - e / 0.4f;
            yield return null;
        }

        toast.alpha = 0f;
        SetActive(toast.gameObject, false);
        toastRoutine = null;
    }

    // ------------------- 擊退數 -------------------

    public void AddKill()
    {
        SetKills(KillCount + 1);
    }

    private void SetKills(int count)
    {
        KillCount = count;
        if (killText != null)
            killText.text = $"擊退 <color={Highlight}>{count}</color>";
    }

    // ------------------- 血量控制 -------------------

    private void CacheHearts()
    {
        if (healthImages == null)
            return;

        heartScales = new Vector3[healthImages.Length];
        heartColors = new Color[healthImages.Length];
        for (int i = 0; i < healthImages.Length; i++)
        {
            if (healthImages[i] == null) continue;
            heartScales[i] = healthImages[i].rectTransform.localScale;
            heartColors[i] = healthImages[i].color;
        }
    }

    /// <summary>補滿血量 = healthImages 數量</summary>
    public void ResetHealth()
    {
        CurrentHealth = MaxHealth;
        heartAnimVersion++;

        if (healthImages != null && heartScales != null)
        {
            for (int i = 0; i < healthImages.Length; i++)
            {
                if (healthImages[i] == null) continue;
                healthImages[i].rectTransform.localScale = heartScales[i];
                healthImages[i].color = heartColors[i];
            }
        }

        RefreshHealthUI();
    }

    /// <summary>扣 1 點血，回傳是否死亡（血量 <= 0）</summary>
    public bool DamageBase()
    {
        if (CurrentHealth <= 0)
            return true;

        CurrentHealth--;
        RefreshHealthUI();
        Flash();

        // 剛失去的那顆愛心放大淡出
        if (healthImages != null && CurrentHealth < healthImages.Length && healthImages[CurrentHealth] != null)
            StartCoroutine(LoseHeart(CurrentHealth));

        return CurrentHealth <= 0;
    }

    /// <summary>依照 CurrentHealth 開關血量圖片</summary>
    private void RefreshHealthUI()
    {
        if (healthImages == null) return;

        for (int i = 0; i < healthImages.Length; i++)
        {
            if (healthImages[i] == null) continue;
            healthImages[i].enabled = i < CurrentHealth;
        }
    }

    private IEnumerator LoseHeart(int index)
    {
        int version = heartAnimVersion;
        Image heart = healthImages[index];
        Vector3 baseScale = heartScales != null ? heartScales[index] : Vector3.one;
        Color baseColor = heartColors != null ? heartColors[index] : Color.white;
        const float duration = 0.35f;

        heart.enabled = true;
        float elapsed = 0f;
        while (elapsed < duration && version == heartAnimVersion)
        {
            float k = elapsed / duration;
            heart.rectTransform.localScale = baseScale * (1f + 0.6f * k);
            heart.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (1f - k));
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 中途被 ResetHealth 打斷時，外觀已由 ResetHealth 還原
        if (version != heartAnimVersion)
            yield break;

        heart.rectTransform.localScale = baseScale;
        heart.color = baseColor;
        heart.enabled = index < CurrentHealth;
    }

    // ------------------- 受擊閃紅 -------------------

    private void Flash()
    {
        if (damageFlash == null)
            return;

        StopFlash();
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private void StopFlash()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
        SetFlashAlpha(0f);
    }

    private IEnumerator FlashRoutine()
    {
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            SetFlashAlpha(flashAlpha * (1f - elapsed / flashDuration));
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetFlashAlpha(0f);
        flashRoutine = null;
    }

    private void SetFlashAlpha(float alpha)
    {
        if (damageFlash == null)
            return;

        Color c = damageFlash.color;
        c.a = alpha;
        damageFlash.color = c;
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }
}
