using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 萬年龜：朝神碑前進；點一下選取、再點一下擊退；碰到神碑就讓神碑扣一點血。
/// 走路動畫是程序化的：四肢對角交替擺動、殼隨步伐起伏擺動、頭跟著點，步伐依實際移動距離推進。
/// </summary>
public class EnemyController : MonoBehaviour
{
    /// <summary>場上所有萬年龜（再玩一次與勝利清場用）</summary>
    public static readonly List<EnemyController> Active = new List<EnemyController>();

    public Transform target;
    public float speed = 0.01f;

    [Header("選取框（子物件 Plane）")]
    public GameObject selectPlane;

    [Header("擊退動畫")]
    [SerializeField] private float popDuration = 0.18f;

    [Header("選取後脈動（提示再點一下就能擊退）")]
    [SerializeField] private float selectedPulse = 0.06f;
    [SerializeField] private float selectedPulseSpeed = 12f;

    [Header("走路動畫")]
    [Tooltip("殼、頭、四肢的共同父物件")]
    public Transform body;
    public Transform head;
    [Tooltip("四肢：0、1 為一組對角，2、3 為另一組")]
    public Transform[] legs = new Transform[0];
    [Tooltip("走完一個步伐循環前進的距離（模型單位，會乘上萬年龜大小）")]
    [SerializeField] private float strideLength = 1.2f;
    [SerializeField] private float legSwing = 30f;
    [SerializeField] private float bodyBob = 0.035f;
    [SerializeField] private float bodyRoll = 3f;
    [SerializeField] private float headNod = 7f;
    [SerializeField] private float headLook = 12f;
    [Tooltip("出生時從 0 長到原本大小的時間（秒）")]
    [SerializeField] private float spawnGrow = 0.3f;

    /// <summary>種類名稱（SinglePlacementManager.EnemyType.displayName）</summary>
    public string TypeName { get; private set; }
    public bool IsSelected => isSelected;
    public bool IsDying => isDying;
    public Vector3 BaseScale => baseScale;
    public float WalkPhase => walkPhase;
    /// <summary>龜殼的 Renderer（特效與點擊位置用）</summary>
    public Renderer MainRenderer { get; private set; }

    private bool isSelected;
    private bool isDying;
    private Vector3 baseScale = Vector3.one;
    private float grow = 1f;
    private float walkPhase;
    private float gait;
    private float idleSeed;
    private Vector3 bodyRestPos;
    private Quaternion bodyRestRot;
    private Quaternion headRest;
    private Quaternion[] legRest = new Quaternion[0];

    private void Awake()
    {
        baseScale = transform.localScale;
        idleSeed = Random.value * 10f;

        if (selectPlane == null)
        {
            Transform plane = transform.Find("Plane");
            if (plane != null)
                selectPlane = plane.gameObject;
        }

        if (selectPlane != null)
            selectPlane.SetActive(false);

        if (body != null)
        {
            bodyRestPos = body.localPosition;
            bodyRestRot = body.localRotation;
            MainRenderer = body.GetComponentInChildren<Renderer>();
        }
        if (MainRenderer == null)
            MainRenderer = GetComponent<Renderer>();
        if (head != null)
            headRest = head.localRotation;
        legRest = new Quaternion[legs.Length];
        for (int i = 0; i < legs.Length; i++)
            legRest[i] = legs[i] != null ? legs[i].localRotation : Quaternion.identity;
    }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    public void Init(Transform target, float speed, string typeName = null)
    {
        this.target = target;
        this.speed = speed;
        TypeName = typeName;
        baseScale = transform.localScale;   // 生成時已依隨機大小縮放過
        grow = spawnGrow > 0f ? 0f : 1f;
        ApplyScale();
    }

    private void Update()
    {
        if (isDying)
            return;

        grow = spawnGrow > 0f ? Mathf.MoveTowards(grow, 1f, Time.deltaTime / spawnGrow) : 1f;
        ApplyScale();

        // 結算後停在原地，只剩待機動作
        SinglePlacementManager game = SinglePlacementManager.instance;
        bool running = game == null || game.IsRunning;

        float moved = 0f;
        if (running && target != null)
        {
            Vector3 targetPos = new Vector3(target.position.x, transform.position.y, target.position.z);
            Vector3 before = transform.position;
            transform.position = Vector3.MoveTowards(before, targetPos, speed * Time.deltaTime);
            moved = Vector3.Distance(before, transform.position);
        }

        Animate(moved);
    }

    private void ApplyScale()
    {
        float pulse = isSelected ? 1f + selectedPulse * Mathf.Sin(Time.time * selectedPulseSpeed) : 1f;
        float g = 1f - (1f - grow) * (1f - grow);   // ease-out
        transform.localScale = baseScale * (g * pulse);
    }

    private void Animate(float moved)
    {
        float size = Mathf.Max(0.0001f, baseScale.x);
        walkPhase += moved / (strideLength * size) * 2f * Mathf.PI;
        gait = Mathf.MoveTowards(gait, moved > 0.00001f ? 1f : 0f, Time.deltaTime * 4f);

        float s = Mathf.Sin(walkPhase);
        float idle = Time.time + idleSeed;

        // 四肢：對角兩隻同步，繞身體的左右軸前後擺
        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i] == null) continue;
            float side = i < 2 ? 1f : -1f;
            legs[i].localRotation = legRest[i] * Quaternion.Euler(0f, 0f, legSwing * s * side * gait);
        }

        // 殼：每步起伏兩次，左右微微搖擺；停下時慢慢呼吸
        if (body != null)
        {
            float bob = bodyBob * gait * (0.5f - 0.5f * Mathf.Cos(2f * walkPhase))
                        + bodyBob * 0.25f * (1f - gait) * (0.5f + 0.5f * Mathf.Sin(idle * 1.6f));
            body.localPosition = bodyRestPos + Vector3.up * bob;
            body.localRotation = bodyRestRot * Quaternion.Euler(bodyRoll * s * gait, 0f, 0f);
        }

        // 頭：走路時跟著步伐點頭，停下時左右張望
        if (head != null)
        {
            float nod = headNod * Mathf.Sin(2f * walkPhase + 0.8f) * gait + 3f * Mathf.Sin(idle * 1.1f) * (1f - gait);
            float look = headLook * Mathf.Sin(idle * 0.7f) * (1f - gait * 0.8f);
            head.localRotation = headRest * Quaternion.Euler(0f, look, nod);
        }
    }

    // 手機點一下 / 滑鼠點一下
    private void OnMouseDown()
    {
        HandleTap();
    }

    public void HandleTap()
    {
        if (isDying)
            return;

        SinglePlacementManager game = SinglePlacementManager.instance;
        if (game != null && !game.IsRunning)
            return;

        if (!isSelected)
        {
            // 第一下：只顯示選取框
            isSelected = true;
            GameAudio.Play(GameAudio.Sfx.Select);

            if (selectPlane != null)
                selectPlane.SetActive(true);
        }
        else
        {
            // 第二下：擊退
            Kill(true);
        }
    }

    /// <summary>擊退並播放消失動畫；byPlayer 為 false 時不計入擊退數（勝利清場）</summary>
    public void Kill(bool byPlayer)
    {
        if (isDying)
            return;

        isDying = true;

        if (selectPlane != null)
            selectPlane.SetActive(false);

        foreach (Collider col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 特效放在龜殼中央，而不是貼地的 pivot
        Vector3 fxPos = MainRenderer != null ? MainRenderer.bounds.center : transform.position;

        SinglePlacementManager game = SinglePlacementManager.instance;
        if (game != null)
        {
            if (byPlayer)
            {
                GameAudio.Play(GameAudio.Sfx.Kill);
                game.OnEnemyKilled(fxPos);
            }
            else
                game.SpawnKillEffect(fxPos);
        }

        StartCoroutine(PopAndDestroy());
    }

    private IEnumerator PopAndDestroy()
    {
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < popDuration)
        {
            // 先脹大一點再縮到消失
            float k = elapsed / popDuration;
            float s = k < 0.3f
                ? Mathf.Lerp(1f, 1.25f, k / 0.3f)
                : Mathf.Lerp(1.25f, 0f, (k - 0.3f) / 0.7f);
            transform.localScale = startScale * s;
            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isDying || other.transform != target)
            return;

        isDying = true;

        if (SinglePlacementManager.instance != null)
            SinglePlacementManager.instance.OnBaseHit();

        Destroy(gameObject);
    }
}
