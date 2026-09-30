using UnityEngine;

/// <summary>
/// 點擊判定：手指按下時找出要點的萬年龜，交給 EnemyController.HandleTap（第一下選取、第二下擊退）。
/// 射線會穿過神碑，萬年龜走到神碑後面也點得到；沒點正中時，挑手指附近最近的一隻（小隻、走得快的比較好點）。
/// </summary>
public class EnemyTapInput : MonoBehaviour
{
    [Tooltip("沒點正中時，手指離萬年龜多近還算點到（畫面短邊的比例）")]
    public float assistRadius = 0.06f;

    private Camera cam;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private static readonly Vector3[] Corners = new Vector3[8];

    private void Update()
    {
        // 手機上觸控也會模擬出滑鼠事件，有觸控時只看觸控，避免同一下算兩次
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                    Tap(t.position);
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            Tap(Input.mousePosition);
        }
    }

    /// <summary>在畫面座標點一下；回傳點到的萬年龜（沒點到為 null）</summary>
    public EnemyController Tap(Vector2 screenPos)
    {
        EnemyController enemy = Pick(screenPos);
        if (enemy != null)
            enemy.HandleTap();
        return enemy;
    }

    public EnemyController Pick(Vector2 screenPos)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return null;

        // 1. 射線直接打到的萬年龜（神碑等其他碰撞體不擋）；疊在一起時優先已選取的那隻，第二下才擊得到
        Ray ray = cam.ScreenPointToRay(screenPos);
        int count = Physics.RaycastNonAlloc(ray, hits, 100f, ~0, QueryTriggerInteraction.Collide);
        EnemyController best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            EnemyController e = hits[i].collider.GetComponentInParent<EnemyController>();
            if (e == null || e.IsDying)
                continue;
            float score = hits[i].distance - (e.IsSelected ? 1000f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        if (best != null)
            return best;

        // 2. 沒點正中：畫面上離手指最近、且在容許距離內的那隻
        float radius = assistRadius * Mathf.Min(cam.pixelWidth, cam.pixelHeight);
        foreach (EnemyController e in EnemyController.Active)
        {
            if (e.IsDying || !TryGetScreenRect(e, out Rect rect))
                continue;
            float d = DistanceToRect(screenPos, rect);
            if (d > radius)
                continue;
            float score = d - (e.IsSelected ? radius * 0.5f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        return best;
    }

    // 龜殼的包圍盒投影到畫面上的範圍；在鏡頭後面就不算
    private bool TryGetScreenRect(EnemyController e, out Rect rect)
    {
        rect = default;
        Renderer r = e.MainRenderer;
        if (r == null)
            return false;

        Bounds b = r.bounds;
        Vector3 c = b.center, x = new Vector3(b.extents.x, 0f, 0f), y = new Vector3(0f, b.extents.y, 0f), z = new Vector3(0f, 0f, b.extents.z);
        Corners[0] = c - x - y - z; Corners[1] = c + x - y - z; Corners[2] = c - x + y - z; Corners[3] = c + x + y - z;
        Corners[4] = c - x - y + z; Corners[5] = c + x - y + z; Corners[6] = c - x + y + z; Corners[7] = c + x + y + z;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in Corners)
        {
            Vector3 p = cam.WorldToScreenPoint(corner);
            if (p.z <= 0f)
                return false;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static float DistanceToRect(Vector2 p, Rect r)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
