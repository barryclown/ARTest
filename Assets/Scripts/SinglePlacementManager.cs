using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 場景與流程控制：偵測平面 → 放置神碑 → 生成萬年龜 → 勝負結算 → 再玩一次。
/// </summary>
public class SinglePlacementManager : MonoBehaviour
{
    public static SinglePlacementManager instance;

    /// <summary>萬年龜的種類：每種固定顏色（prefab）、體型與速度，讓玩家記得「這個顏色的走得快」</summary>
    [System.Serializable]
    public class EnemyType
    {
        public string displayName;
        public GameObject prefab;
        [Tooltip("固定前進速度（公尺/秒）")]
        public float speed = 0.7f;
        [Tooltip("經過多少比例的時間後開始出現（0 = 開局就有）")]
        [Range(0f, 1f)] public float unlockAt;
        [Tooltip("解鎖後的出現權重")]
        public float weight = 1f;
        [Tooltip("第一次出現時提示字的第二行")]
        public string intro;
    }

    [Header("Placement")]
    public GameObject placementPrefab;
    [Tooltip("偵測到平面後，等幾秒再放神碑")]
    public float delaySeconds = 0.5f;
    [Tooltip("神碑放置後的縮放")]
    public Vector3 placedScale = new Vector3(0.45f, 0.45f, 0.1f);
    [Tooltip("神碑出現時的彈出動畫長度（秒）")]
    public float appearDuration = 0.4f;
    [Tooltip("放好神碑後隱藏已偵測到的平面")]
    public bool hidePlanesAfterPlacement = true;
    [Tooltip("把神碑往上抬，讓底座貼在偵測到的地面上（模型的 pivot 在碑身中間）")]
    public bool snapBaseToGround = true;
    [Tooltip("神碑腳下的結界光環")]
    public GameObject baseAuraPrefab;
    [Tooltip("結界直徑（公尺）")]
    public float auraSize = 1.6f;
    public Color auraFullColor = new Color(1f, 0.82f, 0.4f, 0.9f);
    public Color auraLowColor = new Color(1f, 0.3f, 0.22f, 0.9f);
    public float auraSpinSpeed = 12f;

    [Header("Spawning")]
    public EnemyType[] enemyTypes = new EnemyType[0];
    [Tooltip("所有萬年龜速度的倍率（整體調難度用）")]
    public float speedMultiplier = 1f;
    [Tooltip("開局的生怪間隔（秒）")]
    public float spawnInterval = 5f;
    [Tooltip("倒數結束前的生怪間隔（秒），中間依經過時間線性縮短")]
    public float spawnIntervalEnd = 3f;
    [Tooltip("開局每波隻數（最少, 最多）")]
    public Vector2Int waveSizeStart = new Vector2Int(1, 2);
    [Tooltip("倒數結束前每波隻數（最少, 最多）")]
    public Vector2Int waveSizeEnd = new Vector2Int(2, 3);
    [Tooltip("怪物生成半徑（離神碑幾公尺）")]
    public float SpawnRadius = 3.5f;

    [Header("Spawn Directions")]
    [Tooltip("前方扇形：以「玩家 → 神碑」的延長線為 0 度，從神碑後方左右幾度內出生（最小, 最大）；整段路都在畫面裡")]
    public Vector2 frontArc = new Vector2(8f, 30f);
    [Tooltip("兩側：左右幾度（最小, 最大）；超過 90 度代表稍微靠玩家這一側，但不會到玩家背後")]
    public Vector2 sideArc = new Vector2(45f, 100f);
    [Tooltip("經過多少比例的時間後，萬年龜也會從兩側出現")]
    [Range(0f, 1f)] public float sideUnlockAt = 0.35f;
    [Tooltip("兩側解鎖後，每隻從兩側出現的機率")]
    [Range(0f, 1f)] public float sideChance = 0.4f;
    [Tooltip("同一波的萬年龜之間至少相隔幾度")]
    public float minSpawnSeparation = 14f;

    [Header("Enemy Random Size")]
    [Tooltip("相對 prefab 的縮放倍率（範圍小，同一種萬年龜才認得出來）")]
    public float minScale = 0.92f;
    public float maxScale = 1.08f;

    [Header("Feedback")]
    [Tooltip("擊退萬年龜時的粒子特效")]
    public ParticleSystem killEffectPrefab;
    [Tooltip("神碑受擊時晃動的幅度（公尺）")]
    public float baseShakeAmount = 0.03f;
    public float baseShakeDuration = 0.25f;
    [Tooltip("神碑受擊時手機震動")]
    public bool vibrateOnHit = true;

    [Header("Occlusion")]
    [Tooltip("疊在神碑上，標出神碑擋住的畫面範圍")]
    public Material steleOcclusionMask;
    [Tooltip("疊在萬年龜上，走到神碑後面時透過神碑畫出剪影")]
    public Material occludedSilhouette;

    public bool IsRunning => gameRunning;
    public bool IsPlaced => isPlaced;
    public Transform BaseTransform => placedObject != null ? placedObject.transform : null;

    /// <summary>神碑與結界的共同父物件，放在地面上；AR 模式會掛 ARAnchor，讓它跟著真實地板</summary>
    public Transform BaseRoot => baseRoot;

    private ARPlaneManager arPlaneManager;
    private ARAnchorManager arAnchorManager;
    private Camera arCamera;
    private Transform baseRoot;
    private GameObject placedObject;
    private GameObject aura;
    private Material auraMaterial;
    private Coroutine auraPunch;
    private ARPlane pendingPlane;
    private bool virtualGround;
    private Vector3 virtualBasePosition;
    private Coroutine spawnCoroutine;
    private Coroutine shakeCoroutine;
    private Vector3 steleRestLocal;
    private bool isPlaced;
    private bool gameRunning;
    private bool sidesIntroduced;

    private const int MaxWaveSize = 4;
    private readonly System.Collections.Generic.List<float> waveAngles = new System.Collections.Generic.List<float>(MaxWaveSize);
    private readonly System.Collections.Generic.HashSet<EnemyType> introducedTypes = new System.Collections.Generic.HashSet<EnemyType>();

    // 在 Inspector 調整數值時就先把範圍整理好，Spawn 時不用每次再算
    private void OnValidate()
    {
        if (minScale > maxScale)
        {
            float t = minScale;
            minScale = maxScale;
            maxScale = t;
        }
        if (minScale < 0f) minScale = 0f;
        if (maxScale <= 0f) maxScale = 1f;

        speedMultiplier = Mathf.Max(0f, speedMultiplier);
        if (enemyTypes != null)
        {
            foreach (EnemyType type in enemyTypes)
            {
                if (type == null) continue;
                type.speed = Mathf.Max(0.05f, type.speed);
                type.weight = Mathf.Max(0f, type.weight);
            }
        }

        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        spawnIntervalEnd = Mathf.Max(0.1f, spawnIntervalEnd);
        waveSizeStart = ClampWave(waveSizeStart);
        waveSizeEnd = ClampWave(waveSizeEnd);
    }

    private static Vector2Int ClampWave(Vector2Int wave)
    {
        int max = MaxWaveSize;
        int lo = Mathf.Clamp(wave.x, 0, max);
        int hi = Mathf.Clamp(wave.y, lo, max);
        return new Vector2Int(lo, hi);
    }

    private void Awake()
    {
        instance = this;
        arPlaneManager = FindObjectOfType<ARPlaneManager>();
        arAnchorManager = FindObjectOfType<ARAnchorManager>();
        arCamera = Camera.main;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnEnable()
    {
        if (arPlaneManager != null)
            arPlaneManager.planesChanged += OnPlanesChanged;
    }

    private void OnDisable()
    {
        if (arPlaneManager != null)
            arPlaneManager.planesChanged -= OnPlanesChanged;

        StopSpawning();
    }

    // ------------------- 放置神碑 -------------------

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (isPlaced || args.added.Count == 0)
            return;

        // 先記下第一個平面；教學還沒看完就等 UIManager 通知
        if (pendingPlane == null)
            pendingPlane = args.added[0];

        TryPlace();
    }

    /// <summary>
    /// 教學看完、而且已經掃到平面時才放神碑。兩個條件誰後到，都會呼叫這裡。
    /// </summary>
    public void TryPlace()
    {
        if (isPlaced)
            return;
        if (UIManager.instance != null && !UIManager.instance.IntroFinished)
            return;

        // 不支援 AR 的手機：神碑直接放在模擬地面上
        if (virtualGround)
        {
            isPlaced = true;
            if (UIManager.instance != null)
                UIManager.instance.SetScanStatus(true);
            StartCoroutine(PlaceVirtualWithDelay());
            return;
        }

        // 記下的平面可能已被 AR 合併掉，改用當下還在的平面
        if (pendingPlane == null)
            pendingPlane = FindAnyPlane();
        if (pendingPlane == null)
            return;

        isPlaced = true;
        if (UIManager.instance != null)
            UIManager.instance.SetScanStatus(true);
        StartCoroutine(PlaceObjectWithDelay(pendingPlane));
    }

    /// <summary>不支援 AR 時由 NonARFallback 呼叫：之後不等平面，教學看完就把神碑放在指定位置</summary>
    public void UseVirtualGround(Vector3 position)
    {
        virtualGround = true;
        virtualBasePosition = position;
        if (arPlaneManager != null)
            arPlaneManager.enabled = false;
        TryPlace();
    }

    private IEnumerator PlaceVirtualWithDelay()
    {
        yield return new WaitForSeconds(Mathf.Min(delaySeconds, 1.2f));
        if (placedObject == null)
            PlaceBaseAt(virtualBasePosition, Quaternion.identity);
    }

    private ARPlane FindAnyPlane()
    {
        if (arPlaneManager == null)
            return null;

        foreach (var plane in arPlaneManager.trackables)
            return plane;

        return null;
    }

    private IEnumerator PlaceObjectWithDelay(ARPlane plane)
    {
        yield return new WaitForSeconds(delaySeconds);

        if (placedObject != null)
            yield break;

        if (plane == null)
            plane = FindAnyPlane();

        if (plane == null)
        {
            // 平面消失了，回到等待狀態
            isPlaced = false;
            pendingPlane = null;
            yield break;
        }

        PlaceBaseAt(plane.transform.position, plane.transform.rotation);
    }

    /// <summary>在指定位置放神碑並開局（AR 平面與自動測試都走這裡）</summary>
    public void PlaceBaseAt(Vector3 position, Quaternion rotation)
    {
        if (placementPrefab == null || placedObject != null)
            return;

        isPlaced = true;

        // 神碑與結界都放在同一個貼地的父物件下；AR 修正地圖時整組一起跟著真實地板
        baseRoot = new GameObject("BaseRoot").transform;
        baseRoot.SetPositionAndRotation(position, Quaternion.identity);

        placedObject = Instantiate(placementPrefab, baseRoot);
        placedObject.transform.localPosition = Vector3.zero;
        placedObject.transform.rotation = FacingCamera(position, rotation);
        placedObject.transform.localScale = placedScale;
        steleRestLocal = Vector3.up * (snapBaseToGround ? GroundOffset(placedObject, position.y) : 0f);
        placedObject.transform.localPosition = steleRestLocal;
        StartCoroutine(PopIn(placedObject.transform, placedScale, appearDuration));
        AddOcclusionMask(placedObject);

        SpawnAura();
        SpawnKillEffect(placedObject.transform.position);
        GameAudio.Play(GameAudio.Sfx.Appear);
        AnchorBase();

        // 停用平面偵測以省效能
        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = false;
            if (hidePlanesAfterPlacement)
                SetPlanesVisible(false);
        }

        StartGame();
    }

    // 神碑放下時轉向玩家一次，之後不再跟著轉（跟著轉會讓人覺得神碑在動）
    private Quaternion FacingCamera(Vector3 position, Quaternion fallback)
    {
        if (arCamera == null)
            return fallback;

        Vector3 dir = arCamera.transform.position - position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(-dir, Vector3.up) : fallback;
    }

    // 只有 AR 追蹤中才掛 ARAnchor（模擬場景與自動測試沒有 AR，維持原樣）
    private void AnchorBase()
    {
        if (virtualGround || baseRoot == null || arAnchorManager == null || !arAnchorManager.enabled)
            return;
        if (arAnchorManager.subsystem == null || !arAnchorManager.subsystem.running)
            return;

        ARAnchor anchor = baseRoot.gameObject.AddComponent<ARAnchor>();
        // AR 系統移除錨點時不要連神碑一起刪掉，神碑停在最後的位置繼續玩
        anchor.destroyOnRemoval = false;
    }

    // 模型底部到地面的距離
    private static float GroundOffset(GameObject go, float ground)
    {
        bool found = false;
        float minY = 0f;
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.GetComponent<TMPro.TMP_Text>() != null)
                continue;
            float y = r.bounds.min.y;
            minY = found ? Mathf.Min(minY, y) : y;
            found = true;
        }
        return found ? ground - minY : 0f;
    }

    // 神碑看得到的零件多疊一層遮擋標記（萬年龜剪影只畫在這些地方）
    private void AddOcclusionMask(GameObject stele)
    {
        if (steleOcclusionMask == null)
            return;

        foreach (MeshRenderer r in stele.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled || r.GetComponent<TMPro.TMP_Text>() != null)
                continue;
            Material[] mats = r.sharedMaterials;
            System.Array.Resize(ref mats, mats.Length + 1);
            mats[mats.Length - 1] = steleOcclusionMask;
            r.sharedMaterials = mats;
        }
    }

    private void SpawnAura()
    {
        if (baseAuraPrefab == null)
            return;

        aura = Instantiate(baseAuraPrefab, baseRoot);
        aura.transform.localPosition = Vector3.up * 0.01f;
        aura.transform.localRotation = Quaternion.identity;
        aura.transform.localScale = Vector3.one * auraSize;
        Renderer r = aura.GetComponentInChildren<Renderer>();
        if (r != null)
            auraMaterial = r.material;
        UpdateAuraColor();
    }

    // 結界顏色跟著神碑耐久：滿血金色，越少越紅
    private void UpdateAuraColor()
    {
        if (auraMaterial == null)
            return;

        UIManager ui = UIManager.instance;
        float health = ui != null && ui.MaxHealth > 0 ? (float)ui.CurrentHealth / ui.MaxHealth : 1f;
        auraMaterial.color = Color.Lerp(auraLowColor, auraFullColor, health);
    }

    private IEnumerator PunchAura()
    {
        if (aura == null)
            yield break;

        for (float e = 0f; e < 0.3f && aura != null; e += Time.deltaTime)
        {
            aura.transform.localScale = Vector3.one * auraSize * (1f + 0.25f * (1f - e / 0.3f));
            yield return null;
        }
        if (aura != null)
            aura.transform.localScale = Vector3.one * auraSize;
        auraPunch = null;
    }

    private void SetPlanesVisible(bool visible)
    {
        if (arPlaneManager == null)
            return;

        // 只隱藏不 Destroy：平面歸 ARPlaneManager 管，直接刪掉之後重新偵測會拿到失效的物件
        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(visible);
    }

    // ------------------- 遊戲流程 -------------------

    private void StartGame()
    {
        ClearEnemies();
        sidesIntroduced = false;
        gameRunning = true;

        // 開局就有的種類不另外提示；之後解鎖的，第一次出現時跳提示字
        introducedTypes.Clear();
        if (enemyTypes != null)
        {
            foreach (EnemyType type in enemyTypes)
                if (type != null && type.unlockAt <= 0f)
                    introducedTypes.Add(type);
        }

        if (UIManager.instance != null)
            UIManager.instance.StartGame();

        StopSpawning();
        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    /// <summary>結算畫面的「再玩一次」：神碑留在原地，重置計時、血量與場上的萬年龜</summary>
    public void Retry()
    {
        Time.timeScale = 1f;

        if (placedObject == null)
        {
            ResetPlacement();
            return;
        }

        StopBaseShake();
        // 玩家可能換了位置：重新彈出時再對準玩家一次
        placedObject.transform.localPosition = steleRestLocal;
        placedObject.transform.rotation = FacingCamera(placedObject.transform.position, placedObject.transform.rotation);
        StartCoroutine(PopIn(placedObject.transform, placedScale, appearDuration));
        GameAudio.Play(GameAudio.Sfx.Appear);
        StartGame();
        UpdateAuraColor();
    }

    /// <summary>倒數結束（勝利）或神碑血量歸零（失敗）</summary>
    public void EndGame(bool won)
    {
        if (!gameRunning)
            return;

        gameRunning = false;
        StopSpawning();

        if (won)
        {
            // 撐過倒數：場上剩下的萬年龜一起被收伏
            foreach (var enemy in EnemyController.Active.ToArray())
                enemy.Kill(false);
        }

        if (UIManager.instance != null)
            UIManager.instance.ShowResult(won);
    }

    public void OnBaseHit()
    {
        if (!gameRunning)
            return;

        PlayBaseHitFeedback();
        GameAudio.Play(GameAudio.Sfx.Hit);

        bool isDead = UIManager.instance != null && UIManager.instance.DamageBase();
        UpdateAuraColor();
        if (auraPunch != null)
            StopCoroutine(auraPunch);
        auraPunch = StartCoroutine(PunchAura());

        if (isDead)
            EndGame(false);
    }

    public void OnEnemyKilled(Vector3 position)
    {
        SpawnKillEffect(position);

        if (gameRunning && UIManager.instance != null)
            UIManager.instance.AddKill();
    }

    public void SpawnKillEffect(Vector3 position)
    {
        if (killEffectPrefab == null)
            return;

        // 特效 prefab 設了 Stop Action = Destroy，播完會自己刪掉
        ParticleSystem fx = Instantiate(killEffectPrefab, position, Quaternion.identity);
        fx.Play();
    }

    private void ClearEnemies()
    {
        foreach (var enemy in EnemyController.Active.ToArray())
        {
            // 先停用讓它立刻離開 Active 清單，再刪除
            enemy.gameObject.SetActive(false);
            Destroy(enemy.gameObject);
        }
    }

    private void StopSpawning()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    // ------------------- 生怪 -------------------

    private IEnumerator SpawnLoop()
    {
        while (gameRunning && placedObject != null)
        {
            // 依經過時間提高難度：間隔縮短、每波隻數增加
            float t = UIManager.instance != null ? UIManager.instance.Progress01 : 0f;
            int min = Mathf.RoundToInt(Mathf.Lerp(waveSizeStart.x, waveSizeEnd.x, t));
            int max = Mathf.RoundToInt(Mathf.Lerp(waveSizeStart.y, waveSizeEnd.y, t));
            SpawnWave(Random.Range(min, Mathf.Max(min, max) + 1));

            yield return new WaitForSeconds(Mathf.Lerp(spawnInterval, spawnIntervalEnd, t));
        }
    }

    private void SpawnWave(int count)
    {
        count = Mathf.Clamp(count, 0, MaxWaveSize);
        float progress = UIManager.instance != null ? UIManager.instance.Progress01 : 0f;
        bool sidesOpen = progress >= sideUnlockAt - 0.0001f;

        // 同一波左右交替，並互相隔開，避免疊在一起
        waveAngles.Clear();
        float sign = Random.value < 0.5f ? -1f : 1f;
        for (int i = 0; i < count; i++)
        {
            bool side = sidesOpen && (!sidesIntroduced || Random.value < sideChance);
            if (side && !sidesIntroduced)
            {
                // 兩側剛解鎖：這一隻一定從側面來並跳提示字，玩家才知道規則變了
                sidesIntroduced = true;
                if (UIManager.instance != null)
                    UIManager.instance.ShowToast($"{UIManager.SideToast}\n<size=58%>{UIManager.SideToastSub}</size>", 1.8f);
            }

            float angle = PickSpawnAngle(side ? sideArc : frontArc, sign);
            waveAngles.Add(angle);
            sign = -sign;
            SpawnRandomObject(angle);
        }
    }

    // 在扇形範圍內挑一個角度（正負號決定左右），和同一波已用掉的角度至少隔 minSpawnSeparation
    private float PickSpawnAngle(Vector2 arc, float sign)
    {
        float angle = sign * Random.Range(arc.x, arc.y);
        for (int attempt = 1; attempt < 8 && waveAngles.Any(a => Mathf.Abs(a - angle) < minSpawnSeparation); attempt++)
        {
            if (attempt == 4)
                sign = -sign;   // 這一邊擠不下就換另一邊
            angle = sign * Random.Range(arc.x, arc.y);
        }
        return angle;
    }

    /// <summary>
    /// 生怪的基準方向（0 度）：從玩家往神碑看過去、神碑後方那一側。
    /// 每一波都依玩家當下的位置重新算，所以萬年龜一定從玩家面前或兩側來，不會從背後出現。
    /// </summary>
    public Vector3 SpawnAxis()
    {
        Vector3 basePos = baseRoot != null ? baseRoot.position : Vector3.zero;
        Vector3 away = arCamera != null ? basePos - arCamera.transform.position : Vector3.forward;
        away.y = 0f;
        return away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
    }

    /// <summary>依經過時間與權重挑一種已解鎖的萬年龜</summary>
    private EnemyType PickType()
    {
        if (enemyTypes == null)
            return null;

        float progress = UIManager.instance != null ? UIManager.instance.Progress01 : 0f;

        // 剛解鎖的種類下一隻就登場，玩家才會把新顏色跟提示字連在一起記住
        foreach (EnemyType type in enemyTypes)
            if (IsAvailable(type, progress) && !introducedTypes.Contains(type))
                return type;

        float total = 0f;
        foreach (EnemyType type in enemyTypes)
            if (IsAvailable(type, progress)) total += type.weight;
        if (total <= 0f)
            return null;

        float pick = Random.value * total;
        foreach (EnemyType type in enemyTypes)
        {
            if (!IsAvailable(type, progress)) continue;
            pick -= type.weight;
            if (pick <= 0f) return type;
        }
        return enemyTypes.Last(t => IsAvailable(t, progress));
    }

    private static bool IsAvailable(EnemyType type, float progress)
        => type != null && type.prefab != null && type.weight > 0f && type.unlockAt <= progress + 0.0001f;

    private void SpawnRandomObject(float angle)
    {
        if (!gameRunning || placedObject == null)
            return;

        EnemyType type = PickType();
        if (type == null)
            return;

        if (!introducedTypes.Contains(type))
        {
            introducedTypes.Add(type);
            if (UIManager.instance != null)
                UIManager.instance.ShowToast($"{string.Format(UIManager.NewEnemyToastFormat, type.displayName)}\n<size=58%>{type.intro}</size>", 1.8f);
        }

        float d = SpawnRadius > 0f ? SpawnRadius : 3.5f;
        Vector3 away = SpawnAxis();
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;

        // 玩家貼著神碑站時，靠玩家那一側的角度可能繞到玩家身後，收回到側面
        if (arCamera != null && Mathf.Abs(angle) > 80f &&
            Vector3.Dot(baseRoot.position + dir * d - arCamera.transform.position, away) < 0.5f)
            dir = Quaternion.Euler(0f, Mathf.Sign(angle) * 80f, 0f) * away;

        Vector3 spawnPosition = baseRoot.position + dir * d;   // baseRoot 貼在地面上
        // 模型的前進方向是自己的 -X 軸
        Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
        SpawnEnemy(type, spawnPosition, rot);
    }

    /// <summary>在指定位置生成一隻萬年龜並朝神碑前進（測試也用這個擺位）</summary>
    public EnemyController SpawnEnemy(EnemyType type, Vector3 position, Quaternion rotation)
    {
        if (type == null || type.prefab == null || placedObject == null)
            return null;

        GameObject prefabToSpawn = type.prefab;
        GameObject enemy = Instantiate(prefabToSpawn, position, rotation);

        // 以 prefab 原本的大小為基準縮放，只做小幅變化，種類之間的體型差異才看得出來
        float scaleFactor = Random.Range(minScale, maxScale);
        enemy.transform.localScale = prefabToSpawn.transform.localScale * scaleFactor;

        EnemyController controller = enemy.GetComponent<EnemyController>();
        if (controller == null)
            controller = enemy.AddComponent<EnemyController>();

        controller.Init(placedObject.transform, type.speed * speedMultiplier, type.displayName);
        controller.AddOccludedSilhouette(occludedSilhouette);
        return controller;
    }

    // ------------------- 回饋 -------------------

    private void PlayBaseHitFeedback()
    {
        if (placedObject != null && baseShakeAmount > 0f && baseShakeDuration > 0f)
        {
            StopBaseShake();
            shakeCoroutine = StartCoroutine(ShakeBase(placedObject.transform));
        }

#if UNITY_ANDROID || UNITY_IOS
        if (vibrateOnHit && !Application.isEditor)
            Handheld.Vibrate();
#endif
    }

    private void StopBaseShake()
    {
        if (shakeCoroutine == null)
            return;

        StopCoroutine(shakeCoroutine);
        shakeCoroutine = null;
        if (placedObject != null)
            placedObject.transform.localPosition = steleRestLocal;
    }

    private IEnumerator ShakeBase(Transform target)
    {
        float elapsed = 0f;
        while (elapsed < baseShakeDuration && target != null)
        {
            float strength = baseShakeAmount * (1f - elapsed / baseShakeDuration);
            Vector2 offset = Random.insideUnitCircle * strength;
            target.localPosition = steleRestLocal + new Vector3(offset.x, 0f, offset.y);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (target != null)
            target.localPosition = steleRestLocal;
        shakeCoroutine = null;
    }

    // 由 0 彈到目標大小（EaseOutBack）
    private static IEnumerator PopIn(Transform target, Vector3 finalScale, float duration)
    {
        if (duration <= 0f)
        {
            target.localScale = finalScale;
            yield break;
        }

        const float overshoot = 1.70158f;
        float elapsed = 0f;
        while (elapsed < duration && target != null)
        {
            float k = elapsed / duration - 1f;
            float ease = 1f + (overshoot + 1f) * k * k * k + overshoot * k * k;
            target.localScale = finalScale * ease;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (target != null)
            target.localScale = finalScale;
    }

    private void Update()
    {
        if (aura != null)
            aura.transform.Rotate(0f, auraSpinSpeed * Time.deltaTime, 0f, Space.World);
    }

    /// <summary>移除神碑、回到掃描平面的階段</summary>
    public void ResetPlacement()
    {
        StopSpawning();
        StopBaseShake();
        ClearEnemies();

        // 神碑、結界與 ARAnchor 都在 baseRoot 底下，一起刪掉
        if (baseRoot != null)
            Destroy(baseRoot.gameObject);
        baseRoot = null;
        placedObject = null;
        aura = null;
        auraMaterial = null;

        isPlaced = false;
        gameRunning = false;
        pendingPlane = null;

        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = true;
            SetPlanesVisible(true);
        }

        if (UIManager.instance != null)
            UIManager.instance.ShowScanPrompt();

        TryPlace();
    }
}
