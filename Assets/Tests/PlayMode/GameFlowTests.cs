using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using TMPro;
using Object = UnityEngine.Object;

/// <summary>
/// 一關完整流程：教學 → 放神碑 → 生怪 → 選取/擊退 → 受擊 → 失敗 → 再玩一次 → 撐到倒數結束勝利。
/// 不依賴真的 AR 裝置：放神碑直接呼叫 PlaceBaseAt，平面事件用假的 ARPlane。
/// </summary>
public class GameFlowTests
{
    private readonly List<string> gameErrors = new List<string>();
    private UIManager ui;
    private SinglePlacementManager game;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        gameErrors.Clear();
        Application.logMessageReceived += OnLog;
        Time.timeScale = 1f;

        TestStage.ShutdownXR();
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;

        ui = UIManager.instance;
        game = SinglePlacementManager.instance;
        Assert.NotNull(ui, "場景裡要有 UIManager");
        Assert.NotNull(game, "場景裡要有 SinglePlacementManager");

        // 關掉真的平面偵測與鏡頭追蹤，改由測試控制
        TestStage.Prepare();

        // 走慢一點，避免測試還沒點到就被撞光血
        game.speedMultiplier = 0.25f;
    }

    [TearDown]
    public void TearDown()
    {
        Application.logMessageReceived -= OnLog;
        Time.timeScale = 1f;
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;

        string all = message + "\n" + stack;
        if (all.Contains("SinglePlacementManager") || all.Contains("EnemyController") || all.Contains("UIManager"))
            gameErrors.Add(all);
    }

    [UnityTest]
    public IEnumerator SceneIsWired()
    {
        AssertButton(ui.firstPanel, "NextIntro");
        AssertButton(ui.secondPanel, "NextIntro");
        AssertButton(ui.winPanel, "Retry");
        AssertButton(ui.losePanel, "Retry");
        Assert.NotNull(ui.killText, "HUD 缺擊退數");
        Assert.NotNull(ui.winStatsText, "勝利畫面缺戰績");
        Assert.NotNull(ui.loseStatsText, "失敗畫面缺戰績");
        Assert.NotNull(ui.damageFlash, "缺受擊閃紅");
        Assert.NotNull(game.killEffectPrefab, "缺擊退特效");
        Assert.NotNull(game.baseAuraPrefab, "缺神碑結界");
        Assert.GreaterOrEqual(game.enemyTypes.Length, 4, "萬年龜至少四種");
        Assert.AreEqual(game.enemyTypes.Length, game.enemyTypes.Select(t => t.speed).Distinct().Count(), "每種萬年龜速度要不一樣");
        foreach (var type in game.enemyTypes)
        {
            AssertGlyphs(ui.toastText.font, string.Format(UIManager.NewEnemyToastFormat, type.displayName) + type.intro, "type toast");
            GameObject prefab = type.prefab;
            Assert.NotNull(prefab, type.displayName + " 沒有 prefab");
            Assert.NotNull(prefab.transform.Find("Body/Head/Eyes"), type.displayName + " 的眼睛應是獨立零件");
            Assert.NotNull(prefab.transform.Find("Body/Head/EyeDetails"), type.displayName + " 缺虹膜與瞳孔");
            Assert.AreEqual(4, prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Claws_")), type.displayName + " 的四肢都應有腳爪");
            var ec = prefab.GetComponent<EnemyController>();
            Assert.IsTrue(ec.body != null && ec.head != null && ec.legs.Length == 4 && ec.legs.All(l => l != null), prefab.name + " 的走路骨架沒接好");
            Assert.IsFalse(prefab.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "ARSelectionInteractable"), prefab.name + " 還有 XRI 殘留元件");
        }
        Assert.NotNull(Object.FindObjectOfType<GameAudio>(), "缺音效管理");
        Assert.NotNull(Object.FindObjectOfType<LightFollowCamera>(), "主光應跟著鏡頭");
        GameAudio audio = Object.FindObjectOfType<GameAudio>();
        foreach (AudioClip clip in new[] { audio.select, audio.kill, audio.hit, audio.appear, audio.tick, audio.win, audio.lose, audio.ui, audio.bgm })
            Assert.NotNull(clip, "有音效沒接上");
        Assert.AreEqual(3, ui.MaxHealth);
        Assert.AreEqual("01:00", ui.timerText.text);
        Assert.IsTrue(ui.firstPanel.activeSelf, "開場應顯示故事面板");
        Assert.IsFalse(ui.startPanel.activeSelf, "教學期間不應顯示 HUD");
        Assert.IsTrue(ui.tapHint != null && ui.tapHint.gameObject.activeInHierarchy, "教學期間應顯示點擊提示");

        // 所有 UI 文字（含執行時組出的戰績字串）都要有字，不能出現方框
        foreach (TextMeshProUGUI t in Object.FindObjectsOfType<TextMeshProUGUI>(true))
            AssertGlyphs(t.font, t.text, t.name);
        foreach (string sample in UIManager.RuntimeTextSamples)
        {
            AssertGlyphs(ui.toastText.font, sample, "toast");
            AssertGlyphs(ui.scanStatusText.font, sample, "scanStatus");
            AssertGlyphs(ui.winStatsText.font, sample, "stats");
        }
        yield return null;
        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator PlaneFoundDuringIntro_PlacesBaseOnlyAfterIntro()
    {
        game.delaySeconds = 0.2f;
        var planeGo = new GameObject("FakePlane");
        planeGo.transform.position = TestStage.BasePosition;
        ARPlane plane = planeGo.AddComponent<ARPlane>();

        // 看故事時就掃到平面：不能開局
        InvokePlanesChanged(plane);
        yield return new WaitForSeconds(0.5f);
        Assert.IsFalse(game.IsPlaced, "教學還沒看完不應放神碑");
        Assert.IsTrue(ui.firstPanel.activeSelf);

        ui.NextIntro();
        ui.NextIntro();
        Assert.IsTrue(ui.IntroFinished);
        Assert.IsTrue(ui.thirdPanel.activeSelf);

        Assert.AreEqual(UIManager.ScanFound, ui.scanStatusText.text, "掃到平面後提示應改成「找到地面」");
        yield return WaitFor(() => game.IsRunning, 2f, "教學結束後應在已掃到的平面放神碑並開局");
        Assert.AreEqual(plane.transform.position.x, game.BaseTransform.position.x, 0.001f);
        Assert.AreEqual(plane.transform.position.z, game.BaseTransform.position.z, 0.001f);
        Assert.IsTrue(ui.toast.gameObject.activeSelf, "開局應跳出提示字");
        Assert.NotNull(GameObject.Find("BaseAura(Clone)"), "神碑腳下應有結界光環");
        Assert.NotNull(game.BaseTransform.Find("SteleFace"), "碑面應有「定海神碑」浮雕");
        Assert.AreEqual(0, game.BaseTransform.GetComponentsInChildren<TMP_Text>(true).Length, "舊的碑面小字應移除");

        // 神碑底座應貼在地面上（模型 pivot 在碑身中間，舊版會陷進地面約 0.5 公尺）
        yield return new WaitForSeconds(0.6f);
        float bottom = game.BaseTransform.GetComponentsInChildren<MeshRenderer>()
            .Where(r => r.GetComponent<TMP_Text>() == null).Min(r => r.bounds.min.y);
        Assert.AreEqual(plane.transform.position.y, bottom, 0.03f, "神碑底座應貼地");

        // 舊版固定 4 秒後會把「尋找神碑」面板又打開；確認開局後不會再出現
        yield return new WaitForSeconds(4.5f);
        Assert.IsFalse(ui.thirdPanel.activeSelf, "開局後不應再跳出教學面板");
        Assert.IsTrue(ui.startPanel.activeSelf);
        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator FullLevelLoop()
    {
        // 1. 教學：故事 → 操作說明 → 尋找神碑
        Assert.IsTrue(ui.firstPanel.activeSelf);
        yield return TestShots.Capture("01_story");
        ui.NextIntro();
        yield return null;
        Assert.IsTrue(ui.secondPanel.activeSelf);
        Assert.IsFalse(ui.firstPanel.activeSelf);
        yield return TestShots.Capture("02_controls");
        ui.NextIntro();
        yield return null;
        Assert.IsTrue(ui.thirdPanel.activeSelf);
        yield return TestShots.Capture("03_scan");

        // 2. 放神碑開局
        game.spawnInterval = 0.8f;
        game.spawnIntervalEnd = 0.8f;
        game.SpawnRadius = 2.2f;
        Vector3 basePos = TestStage.BasePosition;
        game.PlaceBaseAt(basePos, Quaternion.identity);
        yield return null;
        Assert.IsTrue(game.IsRunning);
        Assert.IsTrue(ui.startPanel.activeSelf, "開局應顯示 HUD");
        Assert.IsFalse(ui.thirdPanel.activeSelf);
        Assert.AreEqual(ui.MaxHealth, ui.CurrentHealth);
        Assert.AreEqual(0, ui.KillCount);

        // 3. 生怪：大小依 prefab 原本比例縮放
        yield return WaitFor(() => EnemyController.Active.Count >= 2, 5f, "應該開始生成萬年龜");
        foreach (EnemyController e in EnemyController.Active)
        {
            var type = game.enemyTypes.First(t => t.displayName == e.TypeName);
            Assert.AreEqual(0f, type.unlockAt, "開局只會出現開局就解鎖的種類");
            Assert.AreEqual(type.speed * game.speedMultiplier, e.speed, 0.0001f, "每種萬年龜的速度固定");
            float s = e.BaseScale.x / type.prefab.transform.localScale.x;
            Assert.That(s, Is.InRange(game.minScale - 0.001f, game.maxScale + 0.001f), "萬年龜縮放應以 prefab 原尺寸為基準");
        }
        // 走路動畫：四肢會擺動、步伐隨移動推進
        EnemyController walkerA = EnemyController.Active.First();
        Assert.AreEqual(4, walkerA.legs.Length, "萬年龜應有四肢可動");
        Quaternion legBefore = walkerA.legs[0].localRotation;
        float phaseBefore = walkerA.WalkPhase;
        yield return new WaitForSeconds(0.6f);
        if (walkerA != null && !walkerA.IsDying)
        {
            Assert.Greater(walkerA.WalkPhase, phaseBefore, "走路時步伐應推進");
            Assert.Greater(Quaternion.Angle(legBefore, walkerA.legs[0].localRotation), 0.5f, "走路時腳應擺動");
        }

        // 4. 點一下選取、再點一下擊退
        // 挑畫面最中間的那隻，截圖才看得到
        Transform camT = Camera.main.transform;
        EnemyController first = EnemyController.Active
            .OrderByDescending(e => Vector3.Dot(camT.forward, (e.transform.position - camT.position).normalized))
            .First();
        first.HandleTap();
        Assert.IsTrue(first.IsSelected, "第一下應顯示選取框");
        Assert.IsTrue(first.selectPlane.activeSelf);
        yield return TestShots.Capture("04_select");
        first.HandleTap();
        yield return new WaitForSeconds(0.06f);
        yield return TestShots.Capture("05_kill_burst");
        yield return new WaitForSeconds(0.3f);
        Assert.IsTrue(first == null, "第二下應擊退並刪除");
        Assert.AreEqual(1, ui.KillCount);
        StringAssert.Contains(">1<", ui.killText.text);

        // 5. 萬年龜撞到神碑（走物理觸發）→ 扣血、閃紅
        EnemyController walker = EnemyController.Active.First(e => !e.IsDying);
        int before = ui.CurrentHealth;
        walker.transform.position = new Vector3(basePos.x, walker.transform.position.y, basePos.z);
        yield return WaitFor(() => ui.CurrentHealth < before, 2f, "萬年龜碰到神碑應扣血");
        Assert.AreEqual(before - 1, ui.CurrentHealth);
        Assert.Greater(ui.damageFlash.color.a, 0f, "受擊應閃紅");
        yield return TestShots.Capture("06_base_hit");

        // 6. 血量歸零 → 失敗；時間不再凍結，萬年龜原地停下
        while (game.IsRunning)
            game.OnBaseHit();
        yield return null;
        Assert.IsTrue(ui.losePanel.activeSelf, "血量歸零應顯示失敗畫面");
        Assert.IsFalse(ui.startPanel.activeSelf);
        Assert.AreEqual(1f, Time.timeScale, "結算不應凍結時間，按鈕動畫與特效才會動");
        StringAssert.Contains("0 / 3", ui.loseStatsText.text);
        EnemyController frozen = EnemyController.Active.FirstOrDefault();
        if (frozen != null)
        {
            Vector3 p = frozen.transform.position;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(p, frozen.transform.position, "結算後萬年龜應停住");
        }
        yield return new WaitForSeconds(0.4f);
        yield return TestShots.Capture("07_lose");

        // 7. 再玩一次：原地重開
        List<EnemyController> old = EnemyController.Active.ToList();
        ClickRetry(ui.losePanel);
        yield return null;
        Assert.IsTrue(old.All(e => e == null), "再玩一次應清掉上一局的萬年龜");
        Assert.IsTrue(game.IsRunning);
        Assert.IsTrue(ui.startPanel.activeSelf);
        Assert.IsFalse(ui.losePanel.activeSelf);
        Assert.AreEqual(ui.MaxHealth, ui.CurrentHealth);
        Assert.AreEqual(0, ui.KillCount);
        Assert.AreEqual(ui.TotalTime, ui.RemainingTime, 0.1f);
        Assert.IsTrue(game.BaseTransform != null, "神碑應留在原地");

        // 8. 最後 10 秒計時器轉紅
        yield return WaitFor(() => EnemyController.Active.Count >= 1, 3f, "重開後應繼續生怪");
        EnemyController.Active.First().Kill(true);
        SetPrivate(ui, "countdownTime", 8f);
        yield return null;
        Assert.AreNotEqual(Color.white, ui.timerText.color, "最後 10 秒計時器應轉色");
        yield return new WaitForSeconds(0.8f);
        yield return TestShots.Capture("08_final_seconds");

        // 9. 撐到倒數結束 → 勝利，場上萬年龜被收伏
        SetPrivate(ui, "countdownTime", 0.5f);
        yield return WaitFor(() => ui.winPanel.activeSelf, 3f, "倒數結束應顯示勝利畫面");
        Assert.IsFalse(game.IsRunning);
        Assert.AreEqual("00:00", ui.timerText.text);
        StringAssert.Contains(">1</color> 隻", ui.winStatsText.text);
        yield return new WaitForSeconds(0.4f);
        Assert.AreEqual(0, EnemyController.Active.Count, "勝利時場上萬年龜應被清掉");
        yield return TestShots.Capture("09_win");

        // 10. 勝利後也能再玩一次
        ClickRetry(ui.winPanel);
        yield return null;
        Assert.IsTrue(game.IsRunning);
        Assert.IsFalse(ui.winPanel.activeSelf);

        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator LaterTypesUnlockWithIntroToast()
    {
        ui.NextIntro();
        ui.NextIntro();
        game.spawnInterval = game.spawnIntervalEnd = 0.3f;
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        yield return null;

        // 跳到 60% 的時間點：黑龜精（20%）與疾龜（50%）都已解鎖
        SetPrivate(ui, "countdownTime", ui.TotalTime * 0.4f);
        yield return WaitFor(() => EnemyController.Active.Any(e => e.TypeName == "疾龜" || e.TypeName == "黑龜精"), 8f, "解鎖後應出現新種類");
        Assert.IsTrue(ui.toast.gameObject.activeSelf, "新種類第一次出現應跳提示字");
        // 測試直接跳到 60%，兩種同時解鎖、同一波登場，後出現的提示會蓋掉前一個；實際遊玩時兩者相隔 18 秒
        var newcomers = EnemyController.Active.Where(e => e.TypeName == "疾龜" || e.TypeName == "黑龜精").Select(e => e.TypeName).ToList();
        Assert.IsTrue(newcomers.Any(n => ui.toastText.text.Contains(n)), "提示字應是新登場的種類：" + ui.toastText.text);
        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator FallbackModeWorksWithoutAR()
    {
        // 模擬「手機不支援 ARCore」：切到模擬場景，教學看完就在虛擬地面放神碑開局
        NonARFallback fb = Object.FindObjectOfType<NonARFallback>();
        Assert.NotNull(fb, "場景要有 NonARFallback");
        fb.Activate();
        yield return null;
        Assert.IsTrue(NonARFallback.Active);
        Assert.IsTrue(ui.fallbackNotice.gameObject.activeSelf, "應顯示模擬場景說明");
        Assert.NotNull(GameObject.Find("FallbackGround"), "應建立模擬地面");
        var session = Object.FindObjectOfType<ARSession>(true);
        Assert.IsTrue(session == null || !session.enabled, "AR Session 應關閉");
        AssertGlyphs(ui.fallbackNotice.font, NonARFallback.NoticeText, "fallbackNotice");

        ui.NextIntro();
        ui.NextIntro();
        yield return WaitFor(() => game.IsRunning, 3f, "模擬場景應直接放神碑開局");
        Assert.AreEqual(fb.baseDistance, game.BaseTransform.position.z, 0.01f);
        yield return new WaitForSeconds(1.5f);
        yield return TestShots.Capture("10_fallback");
        AssertNoGameErrors();
    }

    // ------------------- helpers -------------------

    private void AssertNoGameErrors()
    {
        Assert.IsEmpty(gameErrors, "遊戲腳本有錯誤：\n" + string.Join("\n---\n", gameErrors));
    }

    private static void AssertGlyphs(TMP_FontAsset font, string text, string where)
    {
        string visible = new string(text.Where(c => !char.IsControl(c) && c != ' ').ToArray());
        if (visible.Length == 0) return;
        bool ok = font.HasCharacters(visible, out uint[] missing, true, false);
        string missingText = missing == null ? "" : new string(missing.Select(u => (char)u).ToArray());
        Assert.IsTrue(ok, $"{where} 的字型 {font.name} 缺字：{missingText}");
    }

    private static void AssertButton(GameObject panel, string method)
    {
        bool found = panel.GetComponentsInChildren<Button>(true).Any(b =>
            Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == method));
        Assert.IsTrue(found, $"{panel.name} 缺少呼叫 {method} 的按鈕");
    }

    private static void ClickRetry(GameObject panel)
    {
        Button retry = panel.GetComponentsInChildren<Button>(true).First(b => b.name == "retryButton");
        retry.onClick.Invoke();
    }

    private void InvokePlanesChanged(ARPlane plane)
    {
        var args = new ARPlanesChangedEventArgs(new List<ARPlane> { plane }, new List<ARPlane>(), new List<ARPlane>());
        MethodInfo handler = typeof(SinglePlacementManager).GetMethod("OnPlanesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        handler.Invoke(game, new object[] { args });
    }

    private static void SetPrivate(object target, string field, object value)
    {
        FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(f, "找不到欄位 " + field);
        f.SetValue(target, value);
    }

    private static IEnumerator WaitFor(Func<bool> condition, float timeout, string message)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end)
                Assert.Fail(message + $"（等了 {timeout} 秒）");
            yield return null;
        }
    }
}
