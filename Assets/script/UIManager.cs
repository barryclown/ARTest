using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    public TextMeshProUGUI timerText;
    public GameObject winPanel;
    public GameObject startPanel;
    public GameObject firstPanel;
    public GameObject secondPanel;
    public GameObject thirdPanel;
    public GameObject losePanel;
    public Image[] healthImages;      // UI 上的血量圖示

    [SerializeField] private float totalTime = 60f;

    private float countdownTime;
    private bool isCounting;

    // 血量邏輯
    public int CurrentHealth { get; private set; }

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        countdownTime = totalTime;
        UpdateTimerUI(countdownTime);
        ResetHealth();          // 一開始把血量填滿
        StartCoroutine(SwitchPanels());
    }

    private void Update()
    {
        if (!isCounting)
            return;

        countdownTime -= Time.deltaTime;
        if (countdownTime <= 0f)
        {
            countdownTime = 0f;
            isCounting = false;
            TimerEnd();
        }

        UpdateTimerUI(countdownTime);
    }

    private IEnumerator SwitchPanels()
    {
        firstPanel.SetActive(true);
        secondPanel.SetActive(false);

        yield return new WaitForSeconds(2f);

        secondPanel.SetActive(true);
        firstPanel.SetActive(false);

        yield return new WaitForSeconds(2f);

        secondPanel.SetActive(false);
        thirdPanel.SetActive(true);
    }

    private void UpdateTimerUI(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    public void StartGame()
    {
        thirdPanel.SetActive(false);
        startPanel.SetActive(true);
        countdownTime = totalTime;
        isCounting = true;
        UpdateTimerUI(countdownTime);

        ResetHealth();      // 開始遊戲時重置血量顯示
    }

    private void TimerEnd()
    {
        startPanel.SetActive(false);
        SinglePlacementManager.instance.StopGame();
        winPanel.SetActive(true);
        Time.timeScale = 0f;
    }

    // ------------------- 血量相關 -------------------

    /// <summary>把血量回滿 = healthImages 數量</summary>
    public void ResetHealth()
    {
        CurrentHealth = healthImages != null ? healthImages.Length : 0;
        RefreshHealthUI();
    }

    /// <summary>扣 1 點血，回傳是否死亡（血量 <= 0）</summary>
    public bool DamageBase()
    {
        if (CurrentHealth <= 0)
            return true;

        CurrentHealth--;
        RefreshHealthUI();
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
}
