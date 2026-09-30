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
    public IEnumerator TurtleBehindSteleShowsSilhouetteAndCanBeTapped()
    {
        // 朋友實機回報：萬年龜走到神碑後面會被擋住、點不到
        ui.NextIntro();
        ui.NextIntro();
        game.spawnInterval = game.spawnIntervalEnd = 999f;   // 開局那一波之後不再自動生怪，只留測試擺的萬年龜
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        yield return null;
        foreach (EnemyController e in EnemyController.Active.ToArray())
        {
            e.gameObject.SetActive(false);
            Object.Destroy(e.gameObject);
        }
        yield return new WaitForSeconds(0.8f);   // 等神碑彈出、轉向鏡頭

        EnemyTapInput tapInput = Object.FindObjectOfType<EnemyTapInput>();
        Assert.NotNull(tapInput, "GameManager 要有 EnemyTapInput");
        Assert.NotNull(game.steleOcclusionMask, "缺神碑遮擋標記材質");
        Assert.NotNull(game.occludedSilhouette, "缺萬年龜剪影材質");
        foreach (MeshRenderer r in game.BaseTransform.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled))
            Assert.Contains(game.steleOcclusionMask, r.sharedMaterials, r.name + " 缺遮擋標記");

        Camera cam = Camera.main;
        Bounds stele = game.BaseTransform.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled)
            .Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        Debug.Log($"[Occlusion] stele bounds size {stele.size}, center {stele.center}");

        // 擺一隻潮龜在神碑正後方（鏡頭 → 神碑的延長線上），停在原地
        Vector3 away = game.BaseTransform.position - cam.transform.position;
        away.y = 0f;
        away.Normalize();
        var tide = game.enemyTypes.First(t => t.displayName == "潮龜");
        EnemyController hidden = game.SpawnEnemy(tide, TestStage.BasePosition + away * 0.55f, Quaternion.LookRotation(-away));
        hidden.speed = 0f;
        yield return new WaitForSeconds(0.5f);

        Vector3 shell = hidden.MainRenderer.bounds.center;
        Vector2 screen = cam.WorldToScreenPoint(shell);
        Assert.IsTrue(Physics.Raycast(cam.ScreenPointToRay(screen), out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide));
        Assert.IsNull(hit.collider.GetComponentInParent<EnemyController>(), "前提：這隻萬年龜要被神碑擋住（射線先打到 " + hit.collider.name + "）");
        Assert.Contains(game.occludedSilhouette, hidden.MainRenderer.sharedMaterials, "萬年龜要疊剪影材質");
        Assert.IsFalse(hidden.selectPlane.GetComponent<MeshRenderer>().sharedMaterials.Contains(game.occludedSilhouette), "選取光圈不加剪影");
        yield return TestShots.Capture("11_behind_stele");

        // 點神碑上萬年龜所在的位置：第一下選取、第二下擊退
        Assert.AreEqual(hidden, tapInput.Tap(screen), "點神碑擋住的位置應點到後面的萬年龜");
        Assert.IsTrue(hidden.IsSelected);
        yield return TestShots.Capture("12_behind_stele_selected");
        tapInput.Tap(screen);
        yield return new WaitForSeconds(0.3f);
        Assert.IsTrue(hidden == null, "第二下應擊退");
        Assert.AreEqual(1, ui.KillCount);

        // 沒點正中但很接近：算點到；離很遠：不算
        EnemyController open = game.SpawnEnemy(tide, TestStage.BasePosition + Vector3.Cross(Vector3.up, away) * 1.2f, Quaternion.identity);
        open.speed = 0f;
        yield return new WaitForSeconds(0.4f);
        Vector2 openScreen = cam.WorldToScreenPoint(open.MainRenderer.bounds.center);
        float shortSide = Mathf.Min(cam.pixelWidth, cam.pixelHeight);
        Rect openRect = ScreenRect(cam, open.MainRenderer.bounds);
        Vector2 near = new Vector2(openRect.xMax + tapInput.assistRadius * shortSide * 0.5f, openScreen.y);
        Vector2 far = new Vector2(openRect.xMax + tapInput.assistRadius * shortSide * 2.5f, openScreen.y);
        Assert.IsNull(tapInput.Pick(far), "離萬年龜很遠的點擊不應算數");
        Assert.AreEqual(open, tapInput.Tap(near), "點在萬年龜旁邊一點點應算點到");
        Assert.IsTrue(open.IsSelected);
        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator SteleFacesPlayerOnceAndStaysPut()
    {
        // 朋友回報神碑「一直在移動」：改成放下時對準玩家一次，之後玩家怎麼走神碑都不動；再玩一次時重新對準
        ui.NextIntro();
        ui.NextIntro();
        game.spawnInterval = game.spawnIntervalEnd = 999f;
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        yield return null;
        ClearTurtles();
        yield return new WaitForSeconds(0.6f);   // 等彈出動畫結束

        Transform stele = game.BaseTransform;
        Camera cam = Camera.main;
        Assert.NotNull(game.BaseRoot, "神碑要放在貼地的 BaseRoot 底下");
        Assert.AreEqual(game.BaseRoot, stele.parent, "神碑的父物件應是 BaseRoot");
        Assert.AreEqual(game.BaseRoot, GameObject.Find("BaseAura(Clone)").transform.parent, "結界也在 BaseRoot 底下，AR 錨點修正時一起移動");
        Assert.AreEqual(TestStage.BasePosition.y, game.BaseRoot.position.y, 0.001f, "BaseRoot 貼在地面上");
        Assert.IsNull(game.BaseRoot.GetComponent<ARAnchor>(), "沒有 AR（測試）時不掛 ARAnchor");
        Assert.Less(FacingError(stele, cam), 3f, "放下時應正對玩家");

        Vector3 pivot0 = stele.position;
        Vector3 visual0 = VisualCenter(stele);
        Quaternion rot0 = stele.rotation;
        float maxPivotMove = 0f, maxVisualMove = 0f, maxTurn = 0f;

        // 鏡頭繞神碑一圈（半徑 2 公尺、高 1.4 公尺）：神碑不跟著轉、也不移動
        for (int deg = 0; deg <= 360; deg += 45)
        {
            float rad = deg * Mathf.Deg2Rad;
            cam.transform.position = pivot0 + new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad)) * 2f + Vector3.up * 1.4f;
            cam.transform.LookAt(pivot0);
            yield return new WaitForSeconds(0.4f);

            maxPivotMove = Mathf.Max(maxPivotMove, Vector3.Distance(stele.position, pivot0));
            Vector3 v = VisualCenter(stele) - visual0;
            v.y = 0f;
            maxVisualMove = Mathf.Max(maxVisualMove, v.magnitude);
            maxTurn = Mathf.Max(maxTurn, Quaternion.Angle(rot0, stele.rotation));
        }
        Debug.Log($"[SteleStay] pivot moved {maxPivotMove:F4} m, visual moved {maxVisualMove:F4} m, turned {maxTurn:F3} deg");
        Assert.Less(maxPivotMove, 0.001f, "玩家移動時神碑座標不應改變");
        Assert.Less(maxVisualMove, 0.001f, "玩家移動時神碑外觀不應移動");
        Assert.Less(maxTurn, 0.01f, "放下後神碑不應再跟著玩家轉");

        // 再玩一次：玩家換了位置，神碑重新對準玩家
        cam.transform.position = pivot0 + new Vector3(2f, 1.4f, 0.5f);
        cam.transform.LookAt(pivot0);
        game.Retry();
        yield return null;
        Assert.Less(FacingError(stele, cam), 3f, "再玩一次時應重新對準玩家");
        Assert.AreEqual(pivot0.x, stele.position.x, 0.001f);
        Assert.AreEqual(pivot0.z, stele.position.z, 0.001f);
        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator SpawnsFollowPlayerAndNeverComeFromBehind()
    {
        // 朋友坐著玩，側面和背後來的萬年龜看不到：改成依玩家位置生怪，前半場只從神碑後方扇形來，後半場加入兩側
        ui.NextIntro();
        ui.NextIntro();
        game.spawnInterval = game.spawnIntervalEnd = 999f;
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        yield return null;
        ClearTurtles();
        Camera cam = Camera.main;
        MethodInfo spawnWave = typeof(SinglePlacementManager).GetMethod("SpawnWave", BindingFlags.Instance | BindingFlags.NonPublic);

        List<float> Angles()
        {
            Vector3 away = game.SpawnAxis();
            return EnemyController.Active.Select(e =>
            {
                Vector3 d = e.transform.position - game.BaseRoot.position;
                d.y = 0f;
                return Vector3.SignedAngle(away, d, Vector3.up);
            }).ToList();
        }

        // 1. 前半場：全部在前方扇形內，距離等於生成半徑，身體朝向神碑
        for (int i = 0; i < 12; i++)
            spawnWave.Invoke(game, new object[] { 3 });
        List<float> early = Angles();
        Debug.Log("[Spawn] early angles: " + string.Join(", ", early.Select(a => a.ToString("F0"))));
        Assert.AreEqual(36, early.Count);
        Assert.IsTrue(early.All(a => Mathf.Abs(a) >= game.frontArc.x - 0.5f && Mathf.Abs(a) <= game.frontArc.y + 0.5f), "前半場應只從前方扇形出現");
        Assert.IsTrue(early.Any(a => a < 0f) && early.Any(a => a > 0f), "左右兩邊都要有");
        foreach (EnemyController e in EnemyController.Active)
        {
            Vector3 toBase = game.BaseRoot.position - e.transform.position;
            toBase.y = 0f;
            Assert.AreEqual(game.SpawnRadius, toBase.magnitude, 0.01f, "出生點離神碑應等於生成半徑");
            Assert.Less(Vector3.Angle(-e.transform.right, toBase), 1f, "萬年龜應面向神碑走");
        }
        Assert.IsFalse(ui.toastText.text.Contains(UIManager.SideToast), "前半場不應跳兩側提示");
        ClearTurtles();
        yield return null;

        // 2. 後半場：第一隻一定從側面來並跳提示；之後有前有側，最多 100 度，不會到玩家背後
        // 先照實際順序讓 20% 解鎖的黑龜精登場過，否則它的登場提示會和兩側提示擠在同一波
        SetPrivate(ui, "countdownTime", ui.TotalTime * 0.75f);
        spawnWave.Invoke(game, new object[] { 1 });
        ClearTurtles();
        yield return null;
        SetPrivate(ui, "countdownTime", ui.TotalTime * (1f - game.sideUnlockAt - 0.05f));
        spawnWave.Invoke(game, new object[] { 1 });
        float firstLate = Angles().Single();
        Assert.GreaterOrEqual(Mathf.Abs(firstLate), game.sideArc.x - 0.5f, "兩側解鎖後第一隻應從側面來");
        StringAssert.Contains(UIManager.SideToast, ui.toastText.text, "兩側解鎖時應跳提示字");
        for (int i = 0; i < 20; i++)
            spawnWave.Invoke(game, new object[] { 3 });
        List<float> late = Angles();
        Debug.Log("[Spawn] late angles: " + string.Join(", ", late.Select(a => a.ToString("F0"))));
        Assert.IsTrue(late.Any(a => Mathf.Abs(a) >= game.sideArc.x - 0.5f), "後半場應有從側面來的");
        Assert.IsTrue(late.Any(a => Mathf.Abs(a) <= game.frontArc.y + 0.5f), "後半場仍有從前方來的");
        Assert.IsTrue(late.All(a => Mathf.Abs(a) <= game.sideArc.y + 0.5f), "不應超過兩側扇形");
        foreach (EnemyController e in EnemyController.Active)
        {
            Vector3 fromCam = e.transform.position - cam.transform.position;
            Vector3 camToBase = game.BaseRoot.position - cam.transform.position;
            fromCam.y = camToBase.y = 0f;
            Assert.Less(Vector3.Angle(camToBase, fromCam), 90f, "萬年龜不應出現在玩家背後");
        }
        ClearTurtles();
        yield return null;

        // 3. 玩家換位置：生怪方向跟著玩家走（以新位置為準仍在前方扇形）
        SetPrivate(ui, "countdownTime", ui.TotalTime);
        cam.transform.position = game.BaseRoot.position + new Vector3(2.5f, 1.5f, 1.5f);
        cam.transform.LookAt(game.BaseRoot.position);
        for (int i = 0; i < 6; i++)
            spawnWave.Invoke(game, new object[] { 2 });
        Assert.IsTrue(Angles().All(a => Mathf.Abs(a) <= game.frontArc.y + 0.5f), "玩家換位置後，生怪方向應以新位置為準");
        AssertNoGameErrors();
    }

    [UnityTest]
    public IEnumerator OffscreenTurtleShowsEdgeArrow()
    {
        ui.NextIntro();
        ui.NextIntro();
        game.spawnInterval = game.spawnIntervalEnd = 999f;
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        yield return null;
        ClearTurtles();
        yield return new WaitForSeconds(0.5f);

        OffscreenIndicators indicators = Object.FindObjectOfType<OffscreenIndicators>();
        Assert.NotNull(indicators, "GameManager 要有 OffscreenIndicators");
        Assert.NotNull(Object.FindObjectOfType<ARAnchorManager>(true), "XR Origin 要有 ARAnchorManager");
        Camera cam = Camera.main;
        var tide = game.enemyTypes.First(t => t.displayName == "潮龜");

        // 畫面中間的萬年龜：不顯示箭頭
        Vector3 axis = game.SpawnAxis();
        EnemyController inView = game.SpawnEnemy(tide, game.BaseRoot.position + axis * 1.5f, Quaternion.identity);
        inView.speed = 0f;
        yield return null;
        yield return null;
        Assert.AreEqual(0, indicators.VisibleCount, "畫面內的萬年龜不應顯示箭頭");

        // 右側畫面外：箭頭貼在畫面右緣、指向右邊
        Vector3 right = Vector3.Cross(Vector3.up, axis);
        // 依實際畫面比例往外推，直到確定在畫面外（批次模式的畫面大小不固定）
        Vector3 OffScreen(float side)
        {
            Vector3 p = game.BaseRoot.position;
            for (float lateral = 2f; lateral < 12f; lateral += 0.25f)
            {
                p = game.BaseRoot.position + right * (side * lateral);
                float x = cam.WorldToViewportPoint(p + Vector3.up * 0.1f).x;
                if (side > 0f ? x > 1.2f : x < -0.2f)
                    break;
            }
            return p;
        }
        EnemyController offRight = game.SpawnEnemy(tide, OffScreen(1f), Quaternion.identity);
        offRight.speed = 0f;
        yield return new WaitForSeconds(0.3f);
        Assert.Greater(cam.WorldToViewportPoint(offRight.MainRenderer.bounds.center).x, 1f, "前提：這隻要在畫面右側外面");
        Assert.AreEqual(1, indicators.VisibleCount, "畫面外的萬年龜應顯示一個箭頭");
        RectTransform arrow = indicators.GetArrow(0);
        Assert.Greater(arrow.anchorMin.x, 0.8f, "箭頭應貼在畫面右緣");
        Assert.Less(arrow.anchorMin.x, 1f, "箭頭應留在畫面內");
        float z = arrow.localEulerAngles.z;
        Assert.That(Mathf.DeltaAngle(z, -90f), Is.InRange(-45f, 45f), "箭頭應指向右邊（" + z + "）");

        // 左側畫面外再一隻：兩個箭頭，一左一右
        EnemyController offLeft = game.SpawnEnemy(tide, OffScreen(-1f), Quaternion.identity);
        offLeft.speed = 0f;
        yield return new WaitForSeconds(0.3f);
        Assert.AreEqual(2, indicators.VisibleCount);
        yield return TestShots.Capture("13_offscreen_arrows");

        // 擊退後箭頭消失
        offRight.Kill(true);
        offLeft.Kill(true);
        yield return new WaitForSeconds(0.3f);
        Assert.AreEqual(0, indicators.VisibleCount, "擊退後箭頭應消失");
        AssertNoGameErrors();
    }

    private void ClearTurtles()
    {
        foreach (EnemyController e in EnemyController.Active.ToArray())
        {
            e.gameObject.SetActive(false);
            Object.Destroy(e.gameObject);
        }
    }

    private static float FacingError(Transform stele, Camera cam)
    {
        Vector3 toCam = cam.transform.position - stele.position;
        toCam.y = 0f;
        return Vector3.Angle(-stele.forward, toCam);
    }

    private static Vector3 VisualCenter(Transform root)
    {
        Bounds b = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled)
            .Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
        return b.center;
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

    private static Rect ScreenRect(Camera cam, Bounds b)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector2 p = cam.WorldToScreenPoint(corner);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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
