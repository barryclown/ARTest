using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 自動試玩錄影：教學 → 掃描放神碑 → 撐過 60 秒勝利 → 再玩一次 → 失守。
/// 命令列帶 -recordDir &lt;資料夾&gt; 才會執行，逐格輸出 JPG 與音效時間表（events.tsv），
/// 再由 Tools/make_gameplay_video.py 合成有聲 MP4。以固定 30 fps 的遊戲時間推進，錄出來不會掉格。
/// </summary>
public class DemoRecording
{
    const int Width = 1080;
    const int Height = 1920;
    const int Fps = 30;

    static readonly string RecordDir = GetArg("-recordDir");

    private UIManager ui;
    private SinglePlacementManager game;
    private Camera cam;
    private RectTransform canvasRect;
    private Sprite rippleSprite;
    private FrameRecorder recorder;
    private readonly StringBuilder events = new StringBuilder();
    private bool botActive;
    private bool botBusy;

    [UnityTest, Timeout(3600000)]
    public IEnumerator RecordFullRun()
    {
        if (string.IsNullOrEmpty(RecordDir))
        {
            Assert.Ignore("沒有 -recordDir，不錄影");
            yield break;
        }

        Random.InitState(20260929);
        TestStage.ShutdownXR();
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;

        ui = UIManager.instance;
        game = SinglePlacementManager.instance;
        cam = TestStage.Prepare();
#if UNITY_EDITOR
        rippleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/tap_ripple.png");
#endif

        // 畫布改成跟著鏡頭渲染，才能一起輸出到 RenderTexture
        Canvas canvas = ui.firstPanel.GetComponentInParent<Canvas>().rootCanvas;
        canvasRect = (RectTransform)canvas.transform;
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.3f;

        string frameDir = Path.Combine(RecordDir, "frames");
        if (Directory.Exists(frameDir))
            Directory.Delete(frameDir, true);
        Directory.CreateDirectory(frameDir);

        GameAudio.Played += OnSfx;
        GameAudio.BgmDucked += OnDuck;
        Time.captureFramerate = Fps;
        yield return null;

        recorder = new GameObject("FrameRecorder").AddComponent<FrameRecorder>();
        recorder.Init(cam, rt, frameDir, Fps);
        Log("bgm_start");

        try
        {
            // ── 教學
            yield return new WaitForSeconds(3.4f);
            yield return TapUI(ui.firstPanel.transform.Find("card").position, ui.NextIntro);
            yield return new WaitForSeconds(5.2f);
            yield return TapUI(ui.secondPanel.transform.Find("card").position, ui.NextIntro);

            // ── 掃描：1.5 秒後「掃到地面」，再過 1.8 秒神碑降臨
            game.delaySeconds = 1.8f;
            yield return new WaitForSeconds(1.5f);
            var planeGo = new GameObject("DemoPlane");
            planeGo.transform.position = TestStage.BasePosition;
            InvokePlanesChanged(planeGo.AddComponent<ARPlane>());
            yield return WaitUntilOrTimeout(() => game.IsRunning, 5f);

            // ── 第一局：守住 60 秒（中間故意漏兩隻，展示受擊回饋）
            botActive = true;
            recorder.StartCoroutine(BotLoop(letThroughWindows: new[] { new Vector2(17f, 24f), new Vector2(37f, 44f) }));
            yield return WaitUntilOrTimeout(() => ui.winPanel.activeSelf || ui.losePanel.activeSelf, 75f);
            botActive = false;
            Assert.IsTrue(ui.winPanel.activeSelf, "示範的第一局應該守住（bot 沒擋好）");
            yield return new WaitForSeconds(4.2f);

            // ── 第二局：再玩一次，擊退兩隻後放手，展示失守
            yield return TapUI(ui.winPanel.transform.Find("retryButton").position, game.Retry);
            botActive = true;
            int killsToMake = 2;
            recorder.StartCoroutine(BotLoop(null, () => ui.KillCount >= killsToMake));
            yield return WaitUntilOrTimeout(() => ui.losePanel.activeSelf, 60f);
            botActive = false;
            yield return new WaitForSeconds(4.5f);
        }
        finally
        {
            GameAudio.Played -= OnSfx;
            GameAudio.BgmDucked -= OnDuck;
            Time.captureFramerate = 0;
            if (recorder != null)
            {
                File.WriteAllText(Path.Combine(RecordDir, "events.tsv"), events.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(RecordDir, "frames.txt"), $"{recorder.FrameCount}\t{Fps}\t{Width}x{Height}");
                Object.Destroy(recorder.gameObject);
            }
        }
    }

    /// <summary>App 圖示素材：神碑與結界的特寫（-iconOut &lt;png 路徑&gt; 才執行）</summary>
    [UnityTest]
    public IEnumerator RenderIcon()
    {
        string outPath = GetArg("-iconOut");
        if (string.IsNullOrEmpty(outPath))
        {
            Assert.Ignore("沒有 -iconOut，不輸出圖示");
            yield break;
        }

        TestStage.ShutdownXR();
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;
        ui = UIManager.instance;
        game = SinglePlacementManager.instance;
        cam = TestStage.Prepare();

        game.waveSizeStart = game.waveSizeEnd = Vector2Int.zero;   // 不生怪
        ui.NextIntro();
        ui.NextIntro();
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        ui.firstPanel.GetComponentInParent<Canvas>().rootCanvas.enabled = false;

        Color navy = new Color(0.043f, 0.086f, 0.14f);
        cam.backgroundColor = navy;
        RenderSettings.fogColor = navy;
        RenderSettings.fogStartDistance = 2.5f;
        RenderSettings.fogEndDistance = 6f;
        cam.fieldOfView = 30f;
        cam.transform.position = TestStage.BasePosition + new Vector3(-2.0f, 0.95f, -2.0f);
        cam.transform.LookAt(TestStage.BasePosition + Vector3.up * 0.38f);
        yield return new WaitForSeconds(1f);   // 等彈出動畫與結界轉到定位

        var rt = new RenderTexture(1024, 1024, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
    }

    /// <summary>萬年龜圖鑑：四種並排的 3/4 正面特寫（-shotDir 才執行）</summary>
    [UnityTest]
    public IEnumerator RenderLineup()
    {
        if (!TestShots.Enabled)
        {
            Assert.Ignore("沒有 -shotDir，不輸出圖鑑");
            yield break;
        }

        TestStage.ShutdownXR();
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;
        ui = UIManager.instance;
        game = SinglePlacementManager.instance;
        cam = TestStage.Prepare();
        ui.firstPanel.GetComponentInParent<Canvas>().rootCanvas.enabled = false;

        // 依速度由慢到快排列；頭（-X）轉向鏡頭並偏一點成 3/4 角
        var types = game.enemyTypes.OrderBy(t => t.speed).ToArray();
        for (int i = 0; i < types.Length; i++)
        {
            float x = (i - (types.Length - 1) / 2f) * 1.55f;
            Object.Instantiate(types[i].prefab, new Vector3(x, 0f, 3f), Quaternion.Euler(0f, -62f, 0f));
        }
        cam.fieldOfView = 34f;
        cam.transform.position = new Vector3(0f, 1.25f, -2.6f);
        cam.transform.LookAt(new Vector3(0f, 0.32f, 3f));
        yield return new WaitForSeconds(0.4f);
        yield return TestShots.Capture("lineup", 1600, 700);
    }

    /// <summary>走路動畫特寫：側面跟拍一隻萬年龜走一個步伐循環（-shotDir 才執行）</summary>
    [UnityTest]
    public IEnumerator RenderWalkCycle()
    {
        if (!TestShots.Enabled)
        {
            Assert.Ignore("沒有 -shotDir，不輸出走路特寫");
            yield break;
        }

        TestStage.ShutdownXR();
        yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        yield return null;
        ui = UIManager.instance;
        game = SinglePlacementManager.instance;
        cam = TestStage.Prepare();
        game.waveSizeStart = game.waveSizeEnd = Vector2Int.zero;
        ui.NextIntro();
        ui.NextIntro();
        game.PlaceBaseAt(TestStage.BasePosition, Quaternion.identity);
        ui.firstPanel.GetComponentInParent<Canvas>().rootCanvas.enabled = false;

        // 從 +X 方向朝神碑走（生怪表裡 +X 方向的朝向是 0 度）
        GameObject prefab = game.enemyTypes.First(t => t.displayName == "潮龜").prefab;
        GameObject turtle = Object.Instantiate(prefab, TestStage.BasePosition + new Vector3(3.5f, 0f, 0f), Quaternion.identity);
        var ec = turtle.GetComponent<EnemyController>();
        ec.Init(game.BaseTransform, 0.6f);
        cam.fieldOfView = 35f;

        float strideTime = 1.2f * prefab.transform.localScale.x / 0.6f;
        for (int i = 0; i < 8; i++)
        {
            yield return new WaitForSeconds(strideTime / 8f);
            Vector3 p = turtle.transform.position;
            cam.transform.position = p + new Vector3(-0.2f, 0.55f, -2.2f);
            cam.transform.LookAt(p + Vector3.up * 0.3f);
            yield return TestShots.Capture($"walk_{i}", 800, 600);
        }
        Assert.Greater(ec.WalkPhase, 3f, "一個步伐循環內步伐應推進");
    }

    // ------------------- 自動試玩 -------------------

    // 放行時段內只放「一隻」指定的萬年龜撞神碑，其他照打；第 i 個時段只在耐久剛好是 3 - i 時生效
    private IEnumerator BotLoop(Vector2[] letThroughWindows, System.Func<bool> stopWhen = null)
    {
        float roundStart = Time.time;
        EnemyController letThrough = null;
        while (botActive && game.IsRunning)
        {
            float t = Time.time - roundStart;
            bool windowOpen = false;
            if (letThroughWindows != null)
            {
                for (int i = 0; i < letThroughWindows.Length; i++)
                {
                    Vector2 w = letThroughWindows[i];
                    if (t >= w.x && t <= w.y && ui.CurrentHealth == ui.MaxHealth - i)
                        windowOpen = true;
                }
            }
            if (!windowOpen)
                letThrough = null;
            else if (letThrough == null || letThrough.IsDying)
                letThrough = PickTarget(null, float.MaxValue);

            bool stopped = stopWhen != null && stopWhen();
            if (!stopped && !botBusy)
            {
                EnemyController target = PickTarget(letThrough, EngageDistance);
                if (target != null)
                    yield return TapEnemyTwice(target);
            }
            yield return null;
        }
    }

    // 萬年龜走到離神碑這麼近才出手（出生半徑 3.5 公尺），畫面上先看得到牠們走一段
    const float EngageDistance = 2.3f;

    private EnemyController PickTarget(EnemyController exclude, float maxDistance)
    {
        Vector3 basePos = game.BaseTransform != null ? game.BaseTransform.position : TestStage.BasePosition;
        return EnemyController.Active
            .Where(e => e != null && e != exclude && !e.IsDying && OnScreen(e))
            .Where(e => HorizontalDistance(e.transform.position, basePos) < maxDistance)
            .OrderBy(e => HorizontalDistance(e.transform.position, basePos))
            .FirstOrDefault();
    }

    private IEnumerator TapEnemyTwice(EnemyController enemy)
    {
        botBusy = true;
        yield return new WaitForSeconds(0.08f);   // 反應時間
        if (enemy != null && !enemy.IsDying && game.IsRunning)
        {
            Ripple(ScreenPoint(enemy));
            enemy.HandleTap();
            yield return new WaitForSeconds(0.24f);
            if (enemy != null && !enemy.IsDying && game.IsRunning)
            {
                Ripple(ScreenPoint(enemy));
                enemy.HandleTap();
            }
            yield return new WaitForSeconds(0.12f);
        }
        botBusy = false;
    }

    private bool OnScreen(EnemyController e)
    {
        Vector3 v = cam.WorldToViewportPoint(Center(e));
        return v.z > 0.3f && v.x > 0.02f && v.x < 0.98f && v.y > 0.05f && v.y < 0.86f;
    }

    private static Vector3 Center(EnemyController e)
    {
        Renderer r = e.MainRenderer;
        return r != null ? r.bounds.center : e.transform.position;
    }

    private Vector2 ScreenPoint(EnemyController e) => cam.WorldToScreenPoint(Center(e));

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ------------------- 點擊示意 -------------------

    private IEnumerator TapUI(Vector3 worldPos, System.Action action)
    {
        Ripple(RectTransformUtility.WorldToScreenPoint(cam, worldPos));
        yield return new WaitForSeconds(0.12f);
        action();
    }

    private void Ripple(Vector2 screen)
    {
        if (rippleSprite == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local);
        var go = new GameObject("tapRipple", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(canvasRect, false);
        go.layer = canvasRect.gameObject.layer;
        var img = go.GetComponent<Image>();
        img.sprite = rippleSprite;
        img.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = local;
        rt.sizeDelta = new Vector2(110, 110);
        recorder.StartCoroutine(AnimateRipple(img));
    }

    private static IEnumerator AnimateRipple(Image img)
    {
        for (float e = 0f; e < 0.4f; e += Time.deltaTime)
        {
            float k = e / 0.4f;
            img.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.4f, k);
            img.color = new Color(1f, 1f, 1f, 0.95f * (1f - k));
            yield return null;
        }
        Object.Destroy(img.gameObject);
    }

    // ------------------- 音軌時間表 -------------------

    private void OnSfx(GameAudio.Sfx sfx) => Log(sfx.ToString());

    private void OnDuck(bool duck) => Log(duck ? "duck" : "unduck");

    private void Log(string name)
    {
        float t = recorder != null ? recorder.FrameCount / (float)Fps : 0f;
        events.Append(t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\t').Append(name).Append('\n');
    }

    // ------------------- helpers -------------------

    private void InvokePlanesChanged(ARPlane plane)
    {
        var args = new ARPlanesChangedEventArgs(new List<ARPlane> { plane }, new List<ARPlane>(), new List<ARPlane>());
        typeof(SinglePlacementManager).GetMethod("OnPlanesChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(game, new object[] { args });
    }

    private static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float timeout)
    {
        float end = Time.time + timeout;
        while (!condition() && Time.time < end)
            yield return null;
    }

    private static string GetArg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}

/// <summary>每幀在 LateUpdate 渲染鏡頭並存成 JPG；鏡頭加一點手持晃動。</summary>
public class FrameRecorder : MonoBehaviour
{
    public int FrameCount { get; private set; }

    private Camera cam;
    private RenderTexture rt;
    private Texture2D tex;
    private string dir;
    private Quaternion baseRotation;
    private Vector3 basePosition;

    public void Init(Camera camera, RenderTexture target, string frameDir, int fps)
    {
        cam = camera;
        rt = target;
        dir = frameDir;
        tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        baseRotation = cam.transform.rotation;
        basePosition = cam.transform.position;
    }

    private void LateUpdate()
    {
        float t = FrameCount / 30f;
        cam.transform.SetPositionAndRotation(
            basePosition + new Vector3(Mathf.Sin(t * 0.37f) * 0.05f, Mathf.Sin(t * 0.51f) * 0.03f, 0f),
            baseRotation * Quaternion.Euler(Mathf.Sin(t * 0.43f) * 0.8f, Mathf.Sin(t * 0.29f) * 1.2f, Mathf.Sin(t * 0.31f) * 0.4f));

        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(dir, $"f_{FrameCount:D5}.jpg"), tex.EncodeToJPG(90));
        FrameCount++;
    }

    private void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }
}
