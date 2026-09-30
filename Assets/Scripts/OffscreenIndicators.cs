using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 畫面外的萬年龜：在畫面邊緣顯示箭頭指向牠，玩家知道該往哪邊轉；快撞到神碑時箭頭閃爍。
/// 箭頭圖形由程式產生，不需要另外的貼圖。
/// </summary>
public class OffscreenIndicators : MonoBehaviour
{
    public Color color = new Color(1f, 0.82f, 0.4f, 0.95f);
    [Tooltip("箭頭大小（畫面短邊的比例）")]
    public float arrowSize = 0.085f;
    [Tooltip("箭頭離畫面左右、下緣的距離（畫面短邊的比例）")]
    public float edgeMargin = 0.07f;
    [Tooltip("畫面上方留給 HUD 的高度（畫面高度的比例）")]
    public float topReserved = 0.14f;
    [Tooltip("萬年龜離神碑多近時箭頭開始閃")]
    public float dangerDistance = 1.2f;

    /// <summary>目前顯示中的箭頭數（測試用）</summary>
    public int VisibleCount { get; private set; }

    private RectTransform container;
    private readonly List<Image> arrows = new List<Image>();
    private Sprite arrowSprite;
    private Camera cam;

    /// <summary>第 i 個顯示中的箭頭（測試用）</summary>
    public RectTransform GetArrow(int i) => arrows[i].rectTransform;

    private void Start()
    {
        cam = Camera.main;
        UIManager ui = UIManager.instance;
        Transform canvas = ui != null && ui.firstPanel != null ? ui.firstPanel.transform.parent : null;
        if (canvas == null)
            return;

        // 放在畫布最底層：HUD、提示字與結算畫面都蓋在箭頭上面
        var go = new GameObject("OffscreenIndicators", typeof(RectTransform));
        container = (RectTransform)go.transform;
        container.SetParent(canvas, false);
        container.SetAsFirstSibling();
        container.anchorMin = Vector2.zero;
        container.anchorMax = Vector2.one;
        container.offsetMin = container.offsetMax = Vector2.zero;
        arrowSprite = CreateArrowSprite();
    }

    private void LateUpdate()
    {
        VisibleCount = 0;
        SinglePlacementManager game = SinglePlacementManager.instance;
        if (container != null && cam != null && game != null && game.IsRunning && game.BaseTransform != null)
        {
            // 位置用鏡頭畫面的像素算、再換成錨點比例，畫布大小怎麼變箭頭都貼在同一個相對位置
            Vector2 px = new Vector2(cam.pixelWidth, cam.pixelHeight);
            float margin = edgeMargin * Mathf.Min(px.x, px.y);
            float canvasShort = Mathf.Min(container.rect.width, container.rect.height);
            Vector3 basePos = game.BaseTransform.position;

            foreach (EnemyController e in EnemyController.Active)
            {
                if (e.IsDying)
                    continue;

                Vector3 world = e.MainRenderer != null ? e.MainRenderer.bounds.center : e.transform.position;
                Vector3 vp = cam.WorldToViewportPoint(world);
                bool behind = vp.z < 0f;
                if (!behind && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f)
                    continue;

                // 從畫面中心指向萬年龜的方向；在鏡頭後方時用鏡頭座標判斷左右
                Vector2 d;
                if (behind)
                {
                    Vector3 local = cam.transform.InverseTransformPoint(world);
                    d = new Vector2(local.x, Mathf.Min(local.y, 0f));
                    if (d.sqrMagnitude < 0.0001f) d = Vector2.down;
                }
                else
                {
                    d = new Vector2((vp.x - 0.5f) * px.x, (vp.y - 0.5f) * px.y);
                }

                Image arrow = GetOrCreate(VisibleCount++, canvasShort);
                Vector2 edge = ClampToEdge(d, px, margin);
                Vector2 anchor = new Vector2(0.5f + edge.x / px.x, 0.5f + edge.y / px.y);
                arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = anchor;
                arrow.rectTransform.anchoredPosition = Vector2.zero;
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);

                Vector3 flat = e.transform.position - basePos;
                flat.y = 0f;
                bool danger = flat.magnitude < dangerDistance;
                Color c = color;
                if (danger)
                    c.a *= 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 9f));
                arrow.color = c;
                arrow.rectTransform.localScale = Vector3.one * (danger ? 1.15f : 1f);
            }
        }

        for (int i = VisibleCount; i < arrows.Count; i++)
        {
            if (arrows[i].gameObject.activeSelf)
                arrows[i].gameObject.SetActive(false);
        }
    }

    // 從畫面中心沿方向 d 射出，停在留了邊距的內框上（上方避開 HUD）
    private Vector2 ClampToEdge(Vector2 d, Vector2 size, float margin)
    {
        float halfW = size.x * 0.5f - margin;
        float top = size.y * 0.5f - topReserved * size.y;
        float bottom = -(size.y * 0.5f - margin);

        float tx = Mathf.Abs(d.x) > 0.0001f ? halfW / Mathf.Abs(d.x) : float.MaxValue;
        float ty = d.y > 0.0001f ? top / d.y : (d.y < -0.0001f ? bottom / d.y : float.MaxValue);
        return d * Mathf.Min(tx, ty);
    }

    private Image GetOrCreate(int index, float shortSide)
    {
        while (arrows.Count <= index)
        {
            var go = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(container, false);
            var img = go.GetComponent<Image>();
            img.sprite = arrowSprite;
            img.raycastTarget = false;
            arrows.Add(img);
        }

        Image arrow = arrows[index];
        if (!arrow.gameObject.activeSelf)
            arrow.gameObject.SetActive(true);
        float px = arrowSize * shortSide;
        arrow.rectTransform.sizeDelta = new Vector2(px, px);
        return arrow;
    }

    // 朝上的箭頭（底部內凹），邊緣做 4×4 超取樣反鋸齒
    private static Sprite CreateArrowSprite()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 tip = new Vector2(32f, 60f), left = new Vector2(5f, 8f), right = new Vector2(59f, 8f), notch = new Vector2(32f, 22f);
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int hit = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        var p = new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        if (InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right))
                            hit++;
                    }
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 16));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (a.x - p.x) * (b.y - p.y) - (b.x - p.x) * (a.y - p.y);
}
