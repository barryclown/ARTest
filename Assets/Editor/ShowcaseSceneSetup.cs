using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// 作品集展示版的場景整理（可重複執行，已存在的物件會就地更新）：
// 1. 資料夾與檔名整理  2. 匯入設定（UI 貼圖、特效貼圖、音效）  3. UI 專用字型並預先烘字
// 4. 教學／掃描／HUD／結算畫面改成統一的卡片式介面  5. 選取光圈、神碑結界、擊退特效
// 6. 音效接線  7. 清掉 VR 範本殘留  8. Build 場景、直式畫面、App 名稱與圖示
// 產生貼圖與音效的腳本在 Tools/generate_ui_art.py、Tools/generate_audio.py。
public static class ShowcaseSceneSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string UiArt = "Assets/Art/UI/";
    const string MatDir = "Assets/Art/Materials/";
    const string AudioDir = "Assets/Audio/";
    const string FxDir = "Assets/Prefabs/FX/";
    const string EnemyDir = "Assets/Prefabs/Enemies/";
    const string FontDir = "Assets/Fonts/";
    const string SteleArt = "Assets/Art/Stele/";
    const string TurtlePartsDir = "Assets/Art/Models/TurtleParts/";
    const string SkinDir = "Assets/Art/Models/TurtleSkins/";

    // 萬年龜種類：prefab、配色貼圖（Tools/generate_turtle_skins.py）、體型、固定速度、解鎖時間、權重、登場提示，
    // 以及純色部位：腳爪、眼白、虹膜、是否豎瞳。每種固定顏色＋體型＋速度，玩家看幾次就記得。
    static readonly (string file, string skin, float scale, string name, float speed, float unlockAt, float weight, string intro,
                     string claw, string sclera, string iris, bool slit)[] TurtleTypes =
    {
        ("Turtle_Young", "young", 0.4f, "幼龜", 0.9f, 0f, 3f, "個子小、腳步快", "#EDE6CC", "#FBFBF6", "#3A2716", false),
        ("Turtle_Tide", "tide", 0.5f, "潮龜", 0.6f, 0f, 3f, "從海裡爬上岸的常見種", "#DCE9EC", "#F7FBFC", "#1C4E8A", false),
        ("Turtle_Elder", "elder", 0.6f, "黑龜精", 0.35f, 0.2f, 1.5f, "體型最大、走得最慢", "#2A1C14", "#1B1D1A", "#FFB030", true),
        ("Turtle_Swift", "swift", 0.32f, "疾龜", 1.3f, 0.5f, 2f, "跑得最快，優先處理！", "#EAF9FF", "#FBFAFF", "#19C8E0", false),
    };
    const string BasePrefabPath = "Assets/Prefabs/Placement.prefab";
    static readonly Vector3 PlacedScale = new Vector3(0.45f, 0.45f, 0.1f);   // 與 SinglePlacementManager.placedScale 相同
    const string IconPath = "Assets/Art/Icon/app_icon.png";
    const string IconBgPath = "Assets/Art/Icon/app_icon_bg.png";

    const string Gold = "#F2C45A";
    static readonly Color Navy = Hex("0B1624");
    static readonly Color CardColor = Hex("13233A");
    static readonly Color SubCard = Hex("1C3150");
    static readonly Color GoldC = Hex("E8B84A");
    static readonly Color Cream = Hex("F3E9D2");
    static readonly Color Danger = Hex("E5533D");
    static readonly Color Brown = Hex("2A1606");

    static TMP_FontAsset regular;
    static TMP_FontAsset bold;

    // 面板文案沿用原版內容，只整理換行與標點；寫成常數讓腳本重跑時結果一致
    static readonly string[] StoryParagraphs =
    {
        "傳說中流傳著一隻萬年龜，它在海面興風作浪，掀起驚濤駭浪，船隻覆滅，百姓無處可逃。",
        "如今，海域再次陷入動蕩！偉大的神明，請展現您的神力，收伏這隻惡龜，將平靜重新帶回大海！",
    };
    static readonly string[] ControlsParagraphs =
    {
        "玩家透過手機鏡頭偵測平面，手機畫面上出現神碑。",
        "玩家被選為神明的助手，前來消除復仇的黑龜精一定要守住石坊，否則石坊被破壞掉的話，百姓的船隻以及前來貿易的商船會被擾亂航行，到時候經濟一定會受到重創。",
        "萬年龜群會在神碑周圍生成，玩家需保護神碑不被萬年龜重創。",
        "玩家透過點畫面中出現的萬年龜來守住神碑。",
    };
    const string ScanTitle = "首先尋找要保護的神碑！";
    const string WinText = "身為神明的您\n幫助居民解決了困擾已久的問題\n打敗了萬年龜 往後這個地方一定可以變成繁榮的都市\n讓我們慢慢地看著這座城繁榮吧\n可喜可賀 可喜可賀";
    const string LoseText = "糟糕石坊被破壞掉了，黑龜精又要在這邊搗亂了\n讓我們重新來過，試著再一次地守住我們的石坊\n好讓這條街變得更加繁榮吧！\n加油！";

    [MenuItem("Tools/AR Base/Apply Showcase Scene Setup")]
    public static void Run()
    {
        AssetDatabase.Refresh();
        OrganizeFolders();
        ConfigureImporters();

        regular = EnsureUiFont(FontDir + "NotoSansTC-Regular.ttf", FontDir + "UI/NotoSansTC-Regular UI.asset");
        bold = EnsureUiFont(FontDir + "Extra/NotoSansTC-Bold.ttf", FontDir + "UI/NotoSansTC-Bold UI.asset");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var ui = Object.FindObjectOfType<UIManager>(true);
        var game = Object.FindObjectOfType<SinglePlacementManager>(true);
        Transform canvas = ui.firstPanel.transform.parent;

        StyleStory(ui);
        StyleControls(ui);
        StyleScan(ui);
        StyleHud(ui);
        StyleResult(ui, game, true);
        StyleResult(ui, game, false);
        BuildOverlays(ui, canvas);

        SetupEffects(game);
        BuildTurtleRigs();
        BuildSteleFace();
        SetupAudio(game);
        SetupARFallback(game);
        SetupTapAndOcclusion(game);
        SetupSpawnAndAnchor(game);
        CleanupScene(ui, game);

        // 所有畫面上的字與執行時會出現的字，先烘進字型（原本的字型 atlas 已滿，新字會變方框）
        BakeGlyphs();

        EditorUtility.SetDirty(ui);
        EditorUtility.SetDirty(game);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        ConfigurePlayer();
        AssetDatabase.SaveAssets();
        Debug.Log("[ShowcaseSceneSetup] done");
    }

    // ------------------- 1. 資料夾 -------------------

    static void OrganizeFolders()
    {
        Move("Assets/script", "Assets/Scripts");
        Move("Assets/Perfab", "Assets/Prefabs");
        Move("Assets/Prefabs/enermy", "Assets/Prefabs/Enemies");
        EnsureFolder("Assets/Art");
        Move("Assets/Graphis", "Assets/Art/Models");
        Move("Assets/Material", "Assets/Art/Materials");
        if (AssetDatabase.GetMainAssetTypeAtPath("Assets/fonts") != null && !Directory.Exists("Assets/Fonts_tmp"))
        {
            // 只差大小寫的改名要繞一圈
            if (new DirectoryInfo("Assets").GetDirectories().Any(d => d.Name == "fonts"))
            {
                Move("Assets/fonts", "Assets/Fonts_tmp");
                Move("Assets/Fonts_tmp", "Assets/Fonts");
            }
        }
        Move("Assets/Fonts/New Folder", "Assets/Fonts/Extra");
        Move("Assets/Scenes/SampleScene.unity", ScenePath);
        Move(EnemyDir + "monster1.prefab", EnemyDir + "Turtle_Young.prefab");
        Move(EnemyDir + "monster2.prefab", EnemyDir + "Turtle_Tide.prefab");
        Move(EnemyDir + "monster.prefab", EnemyDir + "Turtle_Elder.prefab");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "Turtle_Swift.prefab") == null)
            AssetDatabase.CopyAsset(EnemyDir + "Turtle_Young.prefab", EnemyDir + "Turtle_Swift.prefab");
        EnsureFolder("Assets/Fonts/UI");
        EnsureFolder("Assets/Art/Materials");
        EnsureFolder("Assets/Prefabs/FX");
    }

    static void Move(string from, string to)
    {
        if (AssetDatabase.GetMainAssetTypeAtPath(from) == null && !AssetDatabase.IsValidFolder(from))
            return;
        string err = AssetDatabase.MoveAsset(from, to);
        if (!string.IsNullOrEmpty(err))
            Debug.LogWarning($"[ShowcaseSceneSetup] 搬移 {from} → {to}：{err}");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------- 2. 匯入設定 -------------------

    static void ConfigureImporters()
    {
        var sprites = new Dictionary<string, Vector4>
        {
            { "card_fill.png", new Vector4(40, 40, 40, 40) },
            { "card_border.png", new Vector4(40, 40, 40, 40) },
            { "pill.png", new Vector4(31, 31, 31, 31) },
            { "pill_border.png", new Vector4(31, 31, 31, 31) },
            { "divider.png", Vector4.zero },
            { "vignette.png", Vector4.zero },
            { "tap_ripple.png", Vector4.zero },
        };
        foreach (var kv in sprites)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(UiArt + kv.Key);
            if (ti == null) { Debug.LogError("[ShowcaseSceneSetup] 缺貼圖 " + kv.Key); continue; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spriteBorder = kv.Value;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        foreach (string name in new[] { "glow_ring.png", "aura.png" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(UiArt + name);
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = true;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            bool music = path.Contains("bgm");
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            s.quality = 0.6f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = !music;
            ai.SaveAndReimport();
        }

        var face = (TextureImporter)AssetImporter.GetAtPath(SteleArt + "stele_face.png");
        if (face != null)
        {
            face.textureType = TextureImporterType.Default;
            face.alphaIsTransparency = true;
            face.mipmapEnabled = true;
            face.wrapMode = TextureWrapMode.Clamp;
            face.SaveAndReimport();
        }
        var faceNormal = (TextureImporter)AssetImporter.GetAtPath(SteleArt + "stele_face_normal.png");
        if (faceNormal != null)
        {
            faceNormal.textureType = TextureImporterType.NormalMap;
            faceNormal.wrapMode = TextureWrapMode.Clamp;
            faceNormal.SaveAndReimport();
        }

        foreach (string path in new[] { IconPath, IconBgPath })
        {
            var icon = (TextureImporter)AssetImporter.GetAtPath(path);
            if (icon == null) continue;
            icon.textureType = TextureImporterType.Default;
            icon.mipmapEnabled = false;
            icon.textureCompression = TextureImporterCompression.Uncompressed;
            icon.SaveAndReimport();
        }
    }

    // ------------------- 3. 字型 -------------------

    static TMP_FontAsset EnsureUiFont(string ttfPath, string assetPath)
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (fa != null) return fa;

        Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null)
        {
            Debug.LogError("[ShowcaseSceneSetup] 找不到字型檔 " + ttfPath);
            return null;
        }

        // 取樣 64pt、2048 atlas，約可放 700 個中文字；維持 Dynamic，之後改文案也能即時補字
        fa = TMP_FontAsset.CreateFontAsset(font, 64, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
        fa.name = Path.GetFileNameWithoutExtension(assetPath);
        AssetDatabase.CreateAsset(fa, assetPath);
        fa.atlasTextures[0].name = fa.name + " Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = fa.name + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    static void BakeGlyphs()
    {
        var chars = new HashSet<char>();
        void Add(string s)
        {
            foreach (char c in Regex.Replace(s ?? "", "<[^>]+>", ""))
                if (!char.IsControl(c)) chars.Add(c);
        }
        foreach (var t in Object.FindObjectsOfType<TextMeshProUGUI>(true)) Add(t.text);
        foreach (string s in UIManager.RuntimeTextSamples) Add(s);
        foreach (var t in TurtleTypes) { Add(t.name); Add(t.intro); }
        for (char c = ' '; c <= '~'; c++) chars.Add(c);
        Add("，。！？、：；「」（）…・　◆");
        string all = new string(chars.ToArray());

        foreach (var fa in new[] { regular, bold })
        {
            // 只補還沒有的字；重跑時不會重複寫進 atlas
            string needed = new string(all.Where(c => !fa.HasCharacter(c)).ToArray());
            if (needed.Length == 0) continue;
            if (!fa.TryAddCharacters(needed, out string missing) && !string.IsNullOrEmpty(missing.Trim()))
                Debug.LogWarning($"[ShowcaseSceneSetup] {fa.name} 缺字：{missing}");
            EditorUtility.SetDirty(fa);
        }
    }

    // ------------------- 4. 介面 -------------------

    static void StyleStory(UIManager ui)
    {
        Transform p = PrepPanel(ui.firstPanel, 0.84f);
        TapToContinue(p, ui);
        Card(p, "card", new Vector2(0, 20), new Vector2(700, 700));
        Text(p, "title", bold, "萬年龜傳說", 54, GoldC, new Vector2(0, 290), new Vector2(640, 80));
        Divider(p, new Vector2(0, 230), 420);

        TextMeshProUGUI body = BodyText(p);
        body.text = string.Join("\n", StoryParagraphs);
        Layout(body, regular, 33, Cream, new Vector2(0, -20), new Vector2(600, 440), TextAlignmentOptions.Center);
        body.lineSpacing = 22;
        body.paragraphSpacing = 32;
        body.transform.SetAsLastSibling();
    }

    static void StyleControls(UIManager ui)
    {
        Transform p = PrepPanel(ui.secondPanel, 0.84f);
        TapToContinue(p, ui);
        Card(p, "card", new Vector2(0, 10), new Vector2(720, 1120));
        Text(p, "title", bold, "遊戲操作", 54, GoldC, new Vector2(0, 492), new Vector2(640, 80));
        Divider(p, new Vector2(0, 432), 420);

        TextMeshProUGUI body = BodyText(p);
        body.text = string.Join("\n", ControlsParagraphs.Select(s => $"<color={Gold}>◆</color> {s}"));
        Layout(body, regular, 27, Cream, new Vector2(0, 148), new Vector2(620, 520), TextAlignmentOptions.TopLeft);
        body.lineSpacing = 16;
        body.paragraphSpacing = 26;

        Image box = Card(p, "rules", new Vector2(0, -315), new Vector2(620, 300), SubCard, 0.95f, 0.45f);
        Text(p, "rulesText", bold,
            $"<color={Gold}>點一下</color>　鎖定萬年龜（出現金色光圈）\n<color={Gold}>再點一下</color>　擊退\n守住神碑 <color={Gold}>60 秒</color> 就勝利！\n<size=85%>顏色不同的萬年龜，速度也不同</size>",
            30, Cream, new Vector2(0, -315), new Vector2(580, 270)).lineSpacing = 24;
        body.transform.SetAsLastSibling();
        p.Find("rulesText").SetAsLastSibling();
    }

    static void StyleScan(UIManager ui)
    {
        Transform p = PrepPanel(ui.thirdPanel, 0f);
        Image card = Card(p, "card", Vector2.zero, new Vector2(660, 150), CardColor, 0.82f, 0.7f);
        TopAnchor(card.rectTransform, new Vector2(0, -190));

        TextMeshProUGUI title = BodyText(p);
        title.text = ScanTitle;
        Layout(title, bold, 36, GoldC, Vector2.zero, new Vector2(620, 60), TextAlignmentOptions.Center);
        TopAnchor(title.rectTransform, new Vector2(0, -160));
        title.transform.SetAsLastSibling();

        TextMeshProUGUI status = Text(p, "scanStatus", regular, UIManager.ScanSearching, 25, Cream, Vector2.zero, new Vector2(620, 40));
        TopAnchor(status.rectTransform, new Vector2(0, -224));
        ui.scanStatusText = status;

        Image reticle = Img(p, "scanReticle", Sprite("tap_ripple.png"), new Color(GoldC.r, GoldC.g, GoldC.b, 0.9f));
        Center(reticle.rectTransform, new Vector2(0, -40), new Vector2(260, 260));
        ui.scanReticle = reticle.rectTransform;
    }

    static void StyleHud(UIManager ui)
    {
        Transform hud = ui.startPanel.transform;
        hud.name = "HUD";

        Image bar = Img(hud, "hudBar", Sprite("pill.png"), new Color(Navy.r, Navy.g, Navy.b, 0.72f), Image.Type.Sliced);
        TopAnchor(bar.rectTransform, new Vector2(0, -86), new Vector2(764, 108));
        Image border = Img(bar.transform, "border", Sprite("pill_border.png"), new Color(GoldC.r, GoldC.g, GoldC.b, 0.55f), Image.Type.Sliced);
        Stretch(border.rectTransform);
        bar.transform.SetAsFirstSibling();

        for (int i = 0; i < ui.healthImages.Length; i++)
        {
            RectTransform h = ui.healthImages[i].rectTransform;
            TopAnchor(h, new Vector2(-310 + 70 * i, -86), new Vector2(62, 62));
            ui.healthImages[i].raycastTarget = false;
        }

        Layout(ui.timerText, bold, 50, Cream, Vector2.zero, new Vector2(220, 80), TextAlignmentOptions.Center);
        TopAnchor(ui.timerText.rectTransform, new Vector2(0, -86));

        ui.killText = Text(hud, "killText", bold, "擊退 0", 32, Cream, Vector2.zero, new Vector2(200, 60), TextAlignmentOptions.Right);
        TopAnchor(ui.killText.rectTransform, new Vector2(262, -86));
    }

    static void StyleResult(UIManager ui, SinglePlacementManager game, bool won)
    {
        GameObject panelGo = won ? ui.winPanel : ui.losePanel;
        panelGo.name = won ? "winPanel" : "losePanel";
        Transform p = PrepPanel(panelGo, 0.84f);

        Card(p, "card", Vector2.zero, new Vector2(720, 1000));
        Text(p, "title", bold, won ? "守護成功" : "神碑失守", 62, won ? GoldC : Danger, new Vector2(0, 400), new Vector2(640, 90));
        Divider(p, new Vector2(0, 338), 420);

        TextMeshProUGUI body = BodyText(p);
        body.text = won ? WinText : LoseText;
        Layout(body, regular, 27, Cream, new Vector2(0, 170), new Vector2(664, 300), TextAlignmentOptions.Center);
        body.lineSpacing = 20;
        body.transform.SetAsLastSibling();

        Card(p, "statsBox", new Vector2(0, -112), new Vector2(560, 200), SubCard, 0.95f, 0.45f);
        TextMeshProUGUI stats = Text(p, "statsText", bold,
            $"擊退萬年龜　<color={Gold}>0</color> 隻\n神碑耐久　<color={Gold}>3 / 3</color>\n守護時間　<color={Gold}>01:00</color>",
            30, Cream, new Vector2(0, -112), new Vector2(520, 180));
        stats.lineSpacing = 24;
        stats.transform.SetAsLastSibling();
        if (won) ui.winStatsText = stats;
        else ui.loseStatsText = stats;

        Image btnImg = Img(p, "retryButton", Sprite("pill.png"), GoldC, Image.Type.Sliced);
        Center(btnImg.rectTransform, new Vector2(0, -345), new Vector2(360, 100));
        btnImg.raycastTarget = true;
        Button btn = btnImg.GetComponent<Button>() ?? btnImg.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        ColorBlock colors = btn.colors;
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        btn.colors = colors;
        SetListener(btn.onClick, game.Retry);
        TextMeshProUGUI label = Text(btnImg.transform, "label", bold, won ? "再玩一次" : "再試一次", 40, Brown, Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform);
        btnImg.transform.SetAsLastSibling();
    }

    static void BuildOverlays(UIManager ui, Transform canvas)
    {
        // 提示字：開局與最後 10 秒
        Transform toastT = canvas.Find("toast");
        GameObject toastGo = toastT != null ? toastT.gameObject : new GameObject("toast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        toastGo.transform.SetParent(canvas, false);
        toastGo.layer = canvas.gameObject.layer;
        Image toastBg = toastGo.GetComponent<Image>();
        toastBg.sprite = Sprite("pill.png");
        toastBg.type = Image.Type.Sliced;
        toastBg.color = new Color(Navy.r, Navy.g, Navy.b, 0.8f);
        toastBg.raycastTarget = false;
        TopAnchor(toastBg.rectTransform, new Vector2(0, -250), new Vector2(640, 160));
        CanvasGroup group = toastGo.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;
        TextMeshProUGUI toastText = Text(toastGo.transform, "text", bold, string.Format(UIManager.StartToastFormat, 60), 50, GoldC, Vector2.zero, Vector2.zero);
        Stretch(toastText.rectTransform);
        toastText.lineSpacing = 10;
        ui.toast = group;
        ui.toastText = toastText;
        toastGo.SetActive(false);

        // 點擊畫面繼續：貼齊螢幕底部
        TextMeshProUGUI hint = Text(canvas, "tapHint", regular, "點擊畫面繼續", 26, Cream, Vector2.zero, new Vector2(600, 50));
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, 45f);
        ui.tapHint = hint;

        // 不支援 AR 時的模擬場景說明（平常隱藏）
        TextMeshProUGUI notice = Text(canvas, "fallbackNotice", regular, NonARFallback.NoticeText, 22, new Color(Cream.r, Cream.g, Cream.b, 0.7f), Vector2.zero, new Vector2(700, 40));
        notice.rectTransform.anchorMin = notice.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        notice.rectTransform.anchoredPosition = new Vector2(0f, 92f);
        notice.gameObject.SetActive(false);
        ui.fallbackNotice = notice;

        // 受擊暈影
        Image flash = Img(canvas, "damageFlash", Sprite("vignette.png"), new Color(Danger.r, Danger.g, Danger.b, 0f));
        Stretch(flash.rectTransform);
        ui.damageFlash = flash;

        toastGo.transform.SetAsLastSibling();
        hint.transform.SetAsLastSibling();
        notice.transform.SetAsLastSibling();
        flash.transform.SetAsLastSibling();
    }

    static Transform PrepPanel(GameObject panel, float backdropAlpha)
    {
        Transform p = panel.transform;
        p.localScale = Vector3.one;   // 原本整個面板放大 1.35 倍，內容會被推出畫面
        Transform bg = p.Find("Panel");
        if (bg != null)
        {
            var img = bg.GetComponent<Image>();
            img.color = new Color(Navy.r, Navy.g, Navy.b, backdropAlpha);
            img.sprite = null;
            bg.SetAsFirstSibling();
        }
        foreach (TextMeshProUGUI t in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.GetComponentInParent<Button>() == null)
                t.raycastTarget = false;
        }
        Transform oldHint = p.Find("tapHint");
        if (oldHint != null) Object.DestroyImmediate(oldHint.gameObject);
        return p;
    }

    static void TapToContinue(Transform panel, UIManager ui)
    {
        Transform bg = panel.Find("Panel");
        Button btn = bg.GetComponent<Button>() ?? bg.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        SetListener(btn.onClick, ui.NextIntro);
    }

    static TextMeshProUGUI BodyText(Transform panel)
    {
        Transform t = panel.Find("Text (TMP)") ?? panel.Find("body");
        t.name = "body";
        return t.GetComponent<TextMeshProUGUI>();
    }

    // ------------------- 5. 特效 -------------------

    static void SetupEffects(SinglePlacementManager game)
    {
        Material ring = EnsureMaterial("SelectRing.mat", UiArt + "glow_ring.png", Color.white);
        Material auraMat = EnsureMaterial("BaseAura.mat", UiArt + "aura.png", new Color(1f, 0.82f, 0.4f, 0.9f));

        // 萬年龜的選取框：深色圓盤 → 金色光圈，並貼到腳底（原本在地面下 0.3 公尺）
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { EnemyDir.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            Transform plane = root.transform.Find("Plane");
            if (plane != null)
            {
                Vector3 lp = plane.localPosition;
                plane.localPosition = new Vector3(lp.x, 0.02f, lp.z);
                plane.GetComponent<MeshRenderer>().sharedMaterial = ring;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        game.killEffectPrefab = BuildKillBurst();
        game.baseAuraPrefab = BuildAura(auraMat);
        game.minScale = 0.92f;
        game.maxScale = 1.08f;
        game.speedMultiplier = 1f;
        game.enemyTypes = TurtleTypes.Select(t => new SinglePlacementManager.EnemyType
        {
            displayName = t.name,
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + t.file + ".prefab"),
            speed = t.speed,
            unlockAt = t.unlockAt,
            weight = t.weight,
            intro = t.intro,
        }).ToArray();
        game.spawnInterval = 5f;
        game.spawnIntervalEnd = 3f;
        game.waveSizeStart = new Vector2Int(1, 2);
        game.waveSizeEnd = new Vector2Int(2, 3);
    }

    static Material EnsureMaterial(string name, string texturePath, Color color)
    {
        string path = MatDir + name;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Sprites/Default");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------- 萬年龜骨架 -------------------

    // 原模型（turtle.obj）的殼、頭、四肢在匯入時被合成一個 mesh；依相連區塊拆回零件，
    // 各自設好關節位置（腳在髖、頭在頸），讓 EnemyController 做程序化走路動畫。
    // 根物件的 MeshRenderer 保留但關閉，作為重跑時的來源；MeshCollider 仍用完整 mesh。
    static void BuildTurtleRigs()
    {
        EnsureFolder(TurtlePartsDir.TrimEnd('/'));
        Dictionary<string, (Mesh mesh, Vector3 pivot, int submesh)> parts = null;

        foreach (var type in TurtleTypes)
        {
            string path = EnemyDir + type.file + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            root.transform.localScale = Vector3.one * type.scale;
            var mf = root.GetComponent<MeshFilter>();
            var mr = root.GetComponent<MeshRenderer>();
            // 原版的萬年龜 prefab 沒掛 EnemyController，是生怪時才 AddComponent；改成直接掛在 prefab 上，骨架參照才存得住
            var ctrl = root.GetComponent<EnemyController>() ?? root.AddComponent<EnemyController>();
            if (mf == null || mr == null)
            {
                Debug.LogWarning("[ShowcaseSceneSetup] 萬年龜 prefab 結構不符，略過 " + path);
                PrefabUtility.UnloadPrefabContents(root);
                continue;
            }

            parts ??= SplitTurtle(mf.sharedMesh);
            Material shellMat = TurtleTextureMaterial(type.skin, "shell", type.skin == "elder" || type.skin == "swift");
            Material skinMat = TurtleTextureMaterial(type.skin, "skin", false);
            Material clawMat = SolidMaterial($"turtle_{type.skin}_claw", Hex(type.claw.TrimStart('#')), 0.35f);
            Material scleraMat = SolidMaterial($"turtle_{type.skin}_sclera", Hex(type.sclera.TrimStart('#')), 0.6f);
            Material irisMat = SolidMaterial($"turtle_{type.skin}_iris", Hex(type.iris.TrimStart('#')), 0.8f,
                type.skin == "elder" ? Hex(type.iris.TrimStart('#')) * 1.4f : Color.black);
            Material pupilMat = SolidMaterial("turtle_pupil", Hex("0A0A0C"), 0.9f);
            Material shineMat = SolidMaterial("turtle_eye_shine", Color.white, 1f, new Color(0.35f, 0.35f, 0.35f));

            Transform oldBody = root.transform.Find("Body");
            if (oldBody != null) Object.DestroyImmediate(oldBody.gameObject);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            // parent 為 null 時掛在 Body 上、放在關節位置；有 parent（眼睛、腳爪）時跟著父零件動，網格已對齊父零件的軸心
            Transform MakePart(string name, Material mat, Transform parent = null)
            {
                var (mesh, pivot, submesh) = parts[name];
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent ?? body, false);
                go.transform.localPosition = parent == null ? pivot : Vector3.zero;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return go.transform;
            }

            MakePart("Shell", shellMat);
            ctrl.head = MakePart("Head", skinMat);
            MakePart("Eyes", scleraMat, ctrl.head);
            var details = new GameObject("EyeDetails", typeof(MeshFilter), typeof(MeshRenderer));
            details.transform.SetParent(ctrl.head, false);
            details.GetComponent<MeshFilter>().sharedMesh = BuildEyeDetails(type.slit, parts["Head"].Item2);
            details.GetComponent<MeshRenderer>().sharedMaterials = new[] { irisMat, pupilMat, shineMat };
            details.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            string[] legNames = { "Leg_FrontA", "Leg_BackA", "Leg_FrontB", "Leg_BackB" };
            ctrl.legs = legNames.Select(n => MakePart(n, skinMat)).ToArray();
            for (int i = 0; i < legNames.Length; i++)
                if (parts.ContainsKey("Claws_" + legNames[i]))
                    MakePart("Claws_" + legNames[i], clawMat, ctrl.legs[i]);
            ctrl.body = body;
            mr.enabled = false;

            // XR Interaction Toolkit 的 AR 選取元件：沒用到，啟用時還會自己再生一個互動管理器
            foreach (MonoBehaviour mb in root.GetComponents<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Namespace != null && mb.GetType().Namespace.StartsWith("UnityEngine.XR.Interaction"))
                    Object.DestroyImmediate(mb);
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static readonly List<Bounds> eyeBounds = new List<Bounds>();

    // 眼睛細節：在每顆眼球表面生成虹膜、瞳孔（圓瞳或豎瞳）、高光三層球冠，頂點以頸部軸心為原點
    static Mesh BuildEyeDetails(bool slit, Vector3 neck)
    {
        string path = TurtlePartsDir + (slit ? "turtle_eye_details_slit.asset" : "turtle_eye_details_round.asset");
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var subs = new[] { new List<int>(), new List<int>(), new List<int>() };
        foreach (Bounds eye in eyeBounds)
        {
            Vector3 c = eye.center - neck;
            float r = Mathf.Max(eye.extents.x, eye.extents.y, eye.extents.z);
            // 頭朝 -X；眼睛看向前方、略往外側與上方
            Vector3 dir = new Vector3(-1f, 0.12f, 0.35f * Mathf.Sign(eye.center.z)).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, dir).normalized;
            AddCap(verts, norms, subs[0], c, r, dir, 36f, 1.012f, 1f);
            AddCap(verts, norms, subs[1], c, r, dir, slit ? 27f : 17f, 1.02f, slit ? 0.26f : 1f);
            AddCap(verts, norms, subs[2], c, r, (dir + up * 0.45f - side * 0.3f * Mathf.Sign(eye.center.z)).normalized, 6.5f, 1.028f, 1f);
        }

        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.subMeshCount = 3;
        for (int i = 0; i < 3; i++) mesh.SetTriangles(subs[i], i);
        mesh.RecalculateBounds();

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // 球面上的一塊圓形（squash < 1 時變成橫向壓扁的橢圓，用來做豎瞳）
    static void AddCap(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 c, float r, Vector3 dir, float angleDeg, float lift, float squash)
    {
        Vector3 u = Vector3.Cross(dir, Vector3.up).normalized;
        Vector3 w = Vector3.Cross(u, dir).normalized;
        const int rings = 5, seg = 28;
        int start = v.Count;
        v.Add(c + dir * r * lift);
        n.Add(dir);
        for (int i = 1; i <= rings; i++)
        {
            float phi = angleDeg * Mathf.Deg2Rad * i / rings;
            for (int j = 0; j < seg; j++)
            {
                float th = 2f * Mathf.PI * j / seg;
                Vector3 p = (dir * Mathf.Cos(phi) + (u * Mathf.Cos(th) * squash + w * Mathf.Sin(th)) * Mathf.Sin(phi)).normalized;
                v.Add(c + p * r * lift);
                n.Add(p);
            }
        }
        void Tri(int a, int b, int d)
        {
            // 依外法線決定繞行方向，正面朝外
            Vector3 nn = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
            if (Vector3.Dot(nn, v[a] - c) > 0f) { t.Add(a); t.Add(b); t.Add(d); }
            else { t.Add(a); t.Add(d); t.Add(b); }
        }
        for (int j = 0; j < seg; j++)
            Tri(start, start + 1 + j, start + 1 + (j + 1) % seg);
        for (int i = 1; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = start + 1 + (i - 1) * seg + j, b = start + 1 + (i - 1) * seg + (j + 1) % seg;
                int cc = start + 1 + i * seg + j, d = start + 1 + i * seg + (j + 1) % seg;
                Tri(a, cc, b);
                Tri(b, cc, d);
            }
    }

    // 萬年龜配色材質（原本的貼圖與材質不動）
    static Material TurtleTextureMaterial(string skin, string part, bool glow)
    {
        Material mat = LoadOrCreate($"turtle_{skin}_{part}");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SkinDir}{skin}_{part}.png");
        mat.color = Color.white;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", part == "shell" ? 0.45f : 0.3f);
        SetEmission(mat, glow ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{SkinDir}{skin}_shell_emission.png") : null, glow ? Color.white * 1.3f : Color.black);
        return mat;
    }

    static Material SolidMaterial(string name, Color color, float gloss, Color? emission = null)
    {
        Material mat = LoadOrCreate(name);
        mat.mainTexture = null;
        mat.color = color;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", gloss);
        SetEmission(mat, null, emission ?? Color.black);
        return mat;
    }

    static Material LoadOrCreate(string name)
    {
        EnsureFolder(MatDir + "Turtles");
        string path = MatDir + "Turtles/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("Standard");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetEmission(Material mat, Texture2D map, Color color)
    {
        bool on = color.maxColorComponent > 0.001f;
        if (on) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        mat.SetTexture("_EmissionMap", map);
        mat.SetColor("_EmissionColor", color);
    }

    static Dictionary<string, (Mesh, Vector3, int)> SplitTurtle(Mesh source)
    {
        Vector3[] v = source.vertices;
        int n = v.Length;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }

        // 位置相同的頂點視為同一點（法線／UV 接縫會拆成多個頂點）
        var byPos = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < n; i++)
        {
            var key = Vector3Int.RoundToInt(v[i] * 10000f);
            if (byPos.TryGetValue(key, out int j)) Union(j, i); else byPos[key] = i;
        }
        var subOf = new int[n];
        for (int s = 0; s < source.subMeshCount; s++)
        {
            int[] t = source.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
            {
                Union(t[i], t[i + 1]);
                Union(t[i], t[i + 2]);
                subOf[t[i]] = subOf[t[i + 1]] = subOf[t[i + 2]] = s;
            }
        }

        var comps = Enumerable.Range(0, n).GroupBy(Find).Select(g =>
        {
            var idx = g.ToList();
            var b = new Bounds(v[idx[0]], Vector3.zero);
            foreach (int i in idx) b.Encapsulate(v[i]);
            return (idx, b);
        }).ToList();

        var shell = comps.OrderByDescending(c => c.b.size.x * c.b.size.z).First();
        var legs = comps.Where(c => c != shell && c.b.size.y > 0.4f && c.b.size.x < 0.7f && c.b.center.y < shell.b.center.y)
                        .OrderByDescending(c => c.idx.Count).Take(4).ToList();
        var head = comps.Where(c => c != shell && !legs.Contains(c) && c.b.center.x < shell.b.center.x - 0.5f)
                        .OrderByDescending(c => c.idx.Count).First();
        if (legs.Count != 4)
            throw new System.Exception("萬年龜四肢辨識失敗：找到 " + legs.Count + " 隻腳");

        var groups = new Dictionary<string, List<int>> { { "Shell", new List<int>(shell.idx) }, { "Head", new List<int>(head.idx) } };
        eyeBounds.Clear();
        // 腳的命名：頭在 -X。A 組＝前左＋後右，B 組＝前右＋後左（對角同步）
        string LegName((List<int> idx, Bounds b) c)
        {
            bool front = c.b.center.x < shell.b.center.x;
            bool left = c.b.center.z < shell.b.center.z;
            return front ? (left ? "Leg_FrontA" : "Leg_FrontB") : (left ? "Leg_BackB" : "Leg_BackA");
        }
        foreach (var leg in legs) groups[LegName(leg)] = new List<int>(leg.idx);

        // 其餘小零件：貼地的腳趾歸最近的腳，其他（眼睛）歸頭或殼
        foreach (var c in comps)
        {
            if (c == shell || c == head || legs.Contains(c)) continue;
            string owner;
            if (c.b.center.y < legs.Min(l => l.b.min.y) + 0.15f)
            {
                var nearest = legs.OrderBy(l => Vector2.Distance(new Vector2(l.b.center.x, l.b.center.z), new Vector2(c.b.center.x, c.b.center.z))).First();
                owner = "Claws_" + LegName(nearest);
            }
            else if (Vector3.Distance(c.b.center, head.b.center) < Vector3.Distance(c.b.center, shell.b.center))
            {
                owner = "Eyes";
                eyeBounds.Add(c.b);
            }
            else
                owner = "Shell";
            if (!groups.ContainsKey(owner)) groups[owner] = new List<int>();
            groups[owner].AddRange(c.idx);
        }

        // 關節：殼在原點、頭在頸、腳在髖；眼睛跟頭同軸心，腳爪跟所屬的腳同軸心
        var pivots = new Dictionary<string, Vector3>();
        foreach (var kv in groups.Where(g => g.Key != "Eyes" && !g.Key.StartsWith("Claws_")))
        {
            var b = new Bounds(v[kv.Value[0]], Vector3.zero);
            foreach (int i in kv.Value) b.Encapsulate(v[i]);
            pivots[kv.Key] = kv.Key == "Shell" ? Vector3.zero
                           : kv.Key == "Head" ? new Vector3(b.max.x - 0.08f, b.center.y - 0.1f, b.center.z)
                           : new Vector3(b.center.x, b.max.y - 0.12f, b.center.z);
        }
        if (groups.ContainsKey("Eyes")) pivots["Eyes"] = pivots["Head"];
        foreach (string key in groups.Keys.Where(k => k.StartsWith("Claws_")))
            pivots[key] = pivots[key.Substring("Claws_".Length)];

        var result = new Dictionary<string, (Mesh, Vector3, int)>();
        foreach (var kv in groups)
        {
            Vector3 pivot = pivots[kv.Key];
            result[kv.Key] = (SaveSubMesh(source, kv.Value, pivot, TurtlePartsDir + "turtle_" + kv.Key.ToLowerInvariant() + ".asset"), pivot, subOf[kv.Value[0]]);
        }
        return result;
    }

    static Mesh SaveSubMesh(Mesh source, List<int> indices, Vector3 pivot, string assetPath)
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < indices.Count; i++) map[indices[i]] = i;

        Vector3[] v = source.vertices, nrm = source.normals;
        Vector4[] tan = source.tangents;
        Vector2[] uv = source.uv;
        var tris = new List<int>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            int[] t = source.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
                if (map.ContainsKey(t[i]) && map.ContainsKey(t[i + 1]) && map.ContainsKey(t[i + 2]))
                    tris.AddRange(new[] { map[t[i]], map[t[i + 1]], map[t[i + 2]] });
        }

        var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(assetPath) };
        mesh.SetVertices(indices.Select(i => v[i] - pivot).ToList());
        if (nrm.Length == v.Length) mesh.SetNormals(indices.Select(i => nrm[i]).ToList());
        if (tan.Length == v.Length) mesh.SetTangents(indices.Select(i => tan[i]).ToList());
        if (uv.Length == v.Length) mesh.SetUVs(0, indices.Select(i => uv[i]).ToList());
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, assetPath);
        return mesh;
    }

    // ------------------- 神碑正面 -------------------

    // 原本碑面是一整段看不清的小字；改成浮雕描金的「定海神碑」＋雲紋、海浪紋（Tools/generate_stele_art.py）。
    // 碑面中央原本疊了一個凸出 1.7 公分的預設 Cube，會擋住新碑面；只關掉它的外觀，碰撞體保留給萬年龜撞擊判定。
    static void BuildSteleFace()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "SteleFace.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, MatDir + "SteleFace.mat");
        }
        mat.shader = Shader.Find("Standard");
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SteleArt + "stele_face.png");
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SteleArt + "stele_face_normal.png"));
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_BumpScale", 1f);
        // AR 畫面沒有環境反射，金屬度高會發黑；改低金屬度＋微弱自發光，遠看也讀得出金字
        mat.SetFloat("_Metallic", 0.2f);
        mat.SetFloat("_Glossiness", 0.45f);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        mat.SetTexture("_EmissionMap", mat.mainTexture);
        mat.SetColor("_EmissionColor", new Color(0.55f, 0.42f, 0.18f));
        // Standard 的 Cutout 模式
        mat.SetFloat("_Mode", 1f);
        mat.SetFloat("_Cutoff", 0.45f);
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(mat);

        GameObject root = PrefabUtility.LoadPrefabContents(BasePrefabPath);
        Vector3 prefabScale = root.transform.localScale;
        root.transform.localScale = PlacedScale;   // 以執行時的大小計算碑面位置

        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
            Object.DestroyImmediate(t.gameObject);
        MeshRenderer panel = root.GetComponent<MeshRenderer>();
        if (panel != null) panel.enabled = false;

        Transform tablet = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "Cube.001");
        Bounds b = tablet.GetComponent<MeshRenderer>().bounds;

        Transform old = root.transform.Find("SteleFace");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var face = new GameObject("SteleFace", typeof(MeshFilter), typeof(MeshRenderer));
        face.transform.SetParent(root.transform, false);
        face.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 0.0015f);
        face.transform.rotation = root.transform.rotation;
        face.transform.localScale = new Vector3(b.size.x * 0.97f / PlacedScale.x, b.size.y * 0.97f / PlacedScale.y, 1f);
        face.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var fr = face.GetComponent<MeshRenderer>();
        fr.sharedMaterial = mat;
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        root.transform.localScale = prefabScale;
        PrefabUtility.SaveAsPrefabAsset(root, BasePrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static GameObject BuildAura(Material mat)
    {
        var root = new GameObject("BaseAura");
        var quad = new GameObject("quad", typeof(MeshFilter), typeof(MeshRenderer));
        quad.transform.SetParent(root.transform, false);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FxDir + "BaseAura.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static ParticleSystem BuildKillBurst()
    {
        var go = new GameObject("KillBurst");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.25f), new Color(1f, 0.97f, 0.75f));
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 55) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = fade;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        PrefabUtility.SaveAsPrefabAsset(go, FxDir + "KillBurst.prefab");
        Object.DestroyImmediate(go);
        return AssetDatabase.LoadAssetAtPath<ParticleSystem>(FxDir + "KillBurst.prefab");
    }

    // ------------------- 6. 音效 -------------------

    // 不支援 ARCore 的手機也能安裝：ARCore 改成「選用」，AR Session 不自動跳 Play 商店安裝（由 NonARFallback 判斷）
    static void SetupARFallback(SinglePlacementManager game)
    {
        var arcore = AssetDatabase.LoadMainAssetAtPath("Assets/XR/Settings/ARCoreSettings.asset");
        if (arcore != null)
        {
            var so = new SerializedObject(arcore);
            // 0 = Required, 1 = Optional；遊戲沒用到深度 API，Depth 也改成選用
            var req = so.FindProperty("m_Requirement");
            if (req != null) req.intValue = 1;
            var depth = so.FindProperty("m_Depth");
            if (depth != null) depth.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(arcore);
        }

        var session = Object.FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>(true);
        if (session != null)
        {
            session.attemptUpdate = false;
            EditorUtility.SetDirty(session);
        }

        NonARFallback fb = game.GetComponent<NonARFallback>() ?? game.gameObject.AddComponent<NonARFallback>();
        Material ground = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "FallbackGround.mat");
        if (ground == null)
        {
            ground = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(ground, MatDir + "FallbackGround.mat");
        }
        ground.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Models/Materials/Gravel040_1K-PNG_Color.png");
        ground.mainTextureScale = new Vector2(24f, 24f);
        ground.color = new Color(0.7f, 0.68f, 0.64f);
        ground.SetFloat("_Glossiness", 0.15f);
        EditorUtility.SetDirty(ground);
        fb.groundMaterial = ground;
        EditorUtility.SetDirty(fb);
    }

    /// <summary>
    /// 只套用 v1.5 之後新增的步驟（點擊判定、遮擋剪影、生怪方向、ARAnchor、畫面外箭頭）並補字，不重跑整套場景設定
    /// </summary>
    [MenuItem("Tools/AR Base/Apply Incremental Setup")]
    public static void ApplyIncremental()
    {
        regular = EnsureUiFont(FontDir + "NotoSansTC-Regular.ttf", FontDir + "UI/NotoSansTC-Regular UI.asset");
        bold = EnsureUiFont(FontDir + "Extra/NotoSansTC-Bold.ttf", FontDir + "UI/NotoSansTC-Bold UI.asset");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var game = Object.FindObjectOfType<SinglePlacementManager>(true);
        SetupTapAndOcclusion(game);
        SetupSpawnAndAnchor(game);
        BakeGlyphs();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        ConfigurePlayer();
        AssetDatabase.SaveAssets();
        Debug.Log("[ShowcaseSceneSetup] incremental setup done");
    }

    // 生怪改成以玩家位置為準（前方扇形，後半場加入兩側，不從背後出現）；
    // AR 模式放神碑時掛 ARAnchor（要 XR Origin 上有 ARAnchorManager）；畫面外的萬年龜在邊緣顯示箭頭
    static void SetupSpawnAndAnchor(SinglePlacementManager game)
    {
        game.SpawnRadius = 3.5f;
        game.frontArc = new Vector2(8f, 30f);
        game.sideArc = new Vector2(45f, 100f);
        game.sideUnlockAt = 0.35f;
        game.sideChance = 0.4f;
        game.minSpawnSeparation = 14f;

        // ARAnchorManager 必須和 XR Origin 在同一個物件上，平面管理器也掛在那裡
        var planes = Object.FindObjectOfType<UnityEngine.XR.ARFoundation.ARPlaneManager>(true);
        if (planes != null && planes.GetComponent<UnityEngine.XR.ARFoundation.ARAnchorManager>() == null)
            planes.gameObject.AddComponent<UnityEngine.XR.ARFoundation.ARAnchorManager>();

        if (game.GetComponent<OffscreenIndicators>() == null)
            game.gameObject.AddComponent<OffscreenIndicators>();
        EditorUtility.SetDirty(game);
    }

    // 點擊改由 EnemyTapInput 判定（射線穿過神碑＋點不準時找最近的一隻）；
    // 萬年龜走到神碑後面時，透過神碑畫出剪影
    static void SetupTapAndOcclusion(SinglePlacementManager game)
    {
        if (game.GetComponent<EnemyTapInput>() == null)
            game.gameObject.AddComponent<EnemyTapInput>();

        game.steleOcclusionMask = EnsureShaderMaterial("SteleOcclusionMask.mat", "ARBase/SteleOcclusionMask");
        game.occludedSilhouette = EnsureShaderMaterial("OccludedSilhouette.mat", "ARBase/OccludedSilhouette");
        game.occludedSilhouette.SetColor("_XrayColor", new Color(0.55f, 0.92f, 1f, 0.75f));
        EditorUtility.SetDirty(game.occludedSilhouette);
        EditorUtility.SetDirty(game);
    }

    static Material EnsureShaderMaterial(string name, string shaderName)
    {
        string path = MatDir + name;
        Shader shader = Shader.Find(shaderName);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetupAudio(SinglePlacementManager game)
    {
        GameAudio audio = game.GetComponent<GameAudio>() ?? game.gameObject.AddComponent<GameAudio>();
        AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + n + ".wav");
        audio.select = Clip("sfx_select");
        audio.kill = Clip("sfx_kill");
        audio.hit = Clip("sfx_hit");
        audio.appear = Clip("sfx_appear");
        audio.tick = Clip("sfx_tick");
        audio.win = Clip("sfx_win");
        audio.lose = Clip("sfx_lose");
        audio.ui = Clip("sfx_ui");
        audio.bgm = Clip("bgm_loop");
        EditorUtility.SetDirty(audio);
    }

    // ------------------- 7. 清理 -------------------

    static void CleanupScene(UIManager ui, SinglePlacementManager game)
    {
        // XR Interaction Toolkit 的 VR 範本殘留（手把、互動管理器、未使用的放置元件）
        foreach (string name in new[] { "Left Controller", "Right Controller", "XR Interaction Manager" })
        {
            Transform t = Object.FindObjectsOfType<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        foreach (MonoBehaviour mb in game.GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().Name == "ARPlacementInteractable")
                Object.DestroyImmediate(mb);
        }
        game.gameObject.name = "GameManager";

        // 主光跟著鏡頭，正對玩家的碑面才不會背光
        Light sun = Object.FindObjectsOfType<Light>(true).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null && sun.GetComponent<LightFollowCamera>() == null)
            sun.gameObject.AddComponent<LightFollowCamera>();
        ui.firstPanel.name = "storyPanel";
        ui.secondPanel.name = "controlsPanel";
        ui.thirdPanel.name = "scanPanel";
    }

    // ------------------- 8. Player 設定 -------------------

    static void ConfigurePlayer()
    {
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;   // HUD 依直式排版
        PlayerSettings.productName = "守護神碑";
        PlayerSettings.companyName = "barryclown";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.barryclown.guardianstele");
        PlayerSettings.bundleVersion = "1.6";
        PlayerSettings.Android.bundleVersionCode = 7;
        PlayerSettings.SplashScreen.backgroundColor = Navy;
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        var iconBg = AssetDatabase.LoadAssetAtPath<Texture2D>(IconBgPath);
        if (icon != null)
        {
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
#if UNITY_ANDROID
            // Android 8 以上用自適應圖示（背景＋前景兩層），否則有些桌面會把舊式圖示縮進白色圓圈
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKindsForPlatform(BuildTargetGroup.Android))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
                foreach (PlatformIcon pi in icons)
                {
                    if (pi.maxLayerCount >= 2 && iconBg != null)
                        pi.SetTextures(iconBg, icon);
                    else
                        pi.SetTexture(icon);
                }
                PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, icons);
            }
#endif
        }
    }

    // ------------------- 小工具 -------------------

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    static Sprite Sprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(UiArt + file);

    static void SetListener(UnityEvent evt, UnityAction action)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(evt, i);
        UnityEventTools.AddPersistentListener(evt, action);
    }

    static GameObject Child(Transform parent, string name, params System.Type[] components)
    {
        Transform t = parent.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name, components);
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        foreach (System.Type c in components)
            if (go.GetComponent(c) == null) go.AddComponent(c);
        return go;
    }

    static Image Img(Transform parent, string name, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
    {
        var img = Child(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        img.sprite = sprite;
        img.type = type;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Image Card(Transform parent, string name, Vector2 pos, Vector2 size)
        => Card(parent, name, pos, size, CardColor, 0.94f, 0.85f);

    static Image Card(Transform parent, string name, Vector2 pos, Vector2 size, Color fill, float fillAlpha, float borderAlpha)
    {
        Image card = Img(parent, name, Sprite("card_fill.png"), new Color(fill.r, fill.g, fill.b, fillAlpha), Image.Type.Sliced);
        Center(card.rectTransform, pos, size);
        Image border = Img(card.transform, "border", Sprite("card_border.png"), new Color(GoldC.r, GoldC.g, GoldC.b, borderAlpha), Image.Type.Sliced);
        Stretch(border.rectTransform);
        return card;
    }

    static void Divider(Transform parent, Vector2 pos, float width)
    {
        Image d = Img(parent, "divider", Sprite("divider.png"), new Color(GoldC.r, GoldC.g, GoldC.b, 0.9f));
        Center(d.rectTransform, pos, new Vector2(width, 16));
    }

    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color,
        Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var tmp = Child(parent, name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        Layout(tmp, font, size, color, pos, sizeDelta, align);
        tmp.enableWordWrapping = false;
        return tmp;
    }

    static void Layout(TextMeshProUGUI tmp, TMP_FontAsset font, float size, Color color, Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align)
    {
        Center(tmp.rectTransform, pos, sizeDelta);
        tmp.font = font;
        tmp.fontSharedMaterial = font.material;
        tmp.fontSize = size;
        tmp.enableAutoSizing = false;
        tmp.fontStyle = FontStyles.Normal;
        tmp.color = color;
        tmp.alignment = align;
        tmp.margin = Vector4.zero;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.lineSpacing = 0;
        tmp.paragraphSpacing = 0;
    }

    static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
    }

    static void TopAnchor(RectTransform rt, Vector2 pos, Vector2? size = null)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        if (size.HasValue) rt.sizeDelta = size.Value;
        rt.localScale = Vector3.one;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }
}
