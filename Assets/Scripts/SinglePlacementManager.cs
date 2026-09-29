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
    [Tooltip("怪物生成半徑")]
    public float SpawnRadius = 5f;

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

    public bool IsRunning => gameRunning;
    public bool IsPlaced => isPlaced;
    public Transform BaseTransform => placedObject != null ? placedObject.transform : null;

    private ARPlaneManager arPlaneManager;
    private Camera arCamera;
    private GameObject placedObject;
    private GameObject aura;
    private Material auraMaterial;
    private Coroutine auraPunch;
    private float groundY;
    private ARPlane pendingPlane;
    private bool virtualGround;
    private Vector3 virtualBasePosition;
    private Coroutine spawnCoroutine;
    private Coroutine shakeCoroutine;
    private Vector3 baseRestPosition;
    private bool isPlaced;
    private bool gameRunning;

    // 上一次使用的生成方向 (0~3)，避免連續同邊
    private int lastSpawnIndex = -1;
    private readonly int[] spawnOrder = new int[4];
    private readonly System.Collections.Generic.HashSet<EnemyType> introducedTypes = new System.Collections.Generic.HashSet<EnemyType>();

    // 固定四個方向：左、右、前、後（單位向量）
    private static readonly Vector3[] SpawnOffsetDirs =
    {
        new Vector3(-1f, 0f,  0f), // 0: -X
        new Vector3( 1f, 0f,  0f), // 1: +X
        new Vector3( 0f, 0f,  1f), // 2: +Z
        new Vector3( 0f, 0f, -1f)  // 3: -Z
    };

    private static readonly float[] SpawnYRotations =
    {
        180f, // -X
        0f,   // +X
        -90f, // +Z
        90f   // -Z
    };

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
        int max = SpawnOffsetDirs.Length;
        int lo = Mathf.Clamp(wave.x, 0, max);
        int hi = Mathf.Clamp(wave.y, lo, max);
        return new Vector2Int(lo, hi);
    }

    private void Awake()
    {
        instance = this;
        arPlaneManager = FindObjectOfType<ARPlaneManager>();
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
        groundY = position.y;
        placedObject = Instantiate(placementPrefab, position, rotation);
        placedObject.transform.localScale = placedScale;
        baseRestPosition = position + Vector3.up * (snapBaseToGround ? GroundOffset(placedObject, groundY) : 0f);
        placedObject.transform.position = baseRestPosition;
        StartCoroutine(PopIn(placedObject.transform, placedScale, appearDuration));

        SpawnAura();
        SpawnKillEffect(baseRestPosition);
        GameAudio.Play(GameAudio.Sfx.Appear);

        // 停用平面偵測以省效能
        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = false;
            if (hidePlanesAfterPlacement)
                SetPlanesVisible(false);
        }

        StartGame();
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

    private void SpawnAura()
    {
        if (baseAuraPrefab == null)
            return;

        aura = Instantiate(baseAuraPrefab, new Vector3(baseRestPosition.x, groundY + 0.01f, baseRestPosition.z), Quaternion.identity);
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
        lastSpawnIndex = -1;
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
        placedObject.transform.position = baseRestPosition;
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
        int dirCount = SpawnOffsetDirs.Length;
        count = Mathf.Clamp(count, 0, dirCount);

        // 同一波各走不同方向，且不接著上一隻的方向，避免疊在一起
        for (int i = 0; i < dirCount; i++)
            spawnOrder[i] = i;
        for (int i = dirCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (spawnOrder[i], spawnOrder[j]) = (spawnOrder[j], spawnOrder[i]);
        }
        if (dirCount > 1 && spawnOrder[0] == lastSpawnIndex)
            (spawnOrder[0], spawnOrder[dirCount - 1]) = (spawnOrder[dirCount - 1], spawnOrder[0]);

        for (int i = 0; i < count; i++)
            SpawnRandomObject(spawnOrder[i]);

        if (count > 0)
            lastSpawnIndex = spawnOrder[count - 1];
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

    private void SpawnRandomObject(int index)
    {
        if (!gameRunning || placedObject == null)
            return;

        EnemyType type = PickType();
        if (type == null)
            return;
        GameObject prefabToSpawn = type.prefab;

        if (!introducedTypes.Contains(type))
        {
            introducedTypes.Add(type);
            if (UIManager.instance != null)
                UIManager.instance.ShowToast($"{string.Format(UIManager.NewEnemyToastFormat, type.displayName)}\n<size=58%>{type.intro}</size>", 1.8f);
        }

        float d = SpawnRadius > 0f ? SpawnRadius : 5f;

        Vector3 spawnPosition = baseRestPosition + SpawnOffsetDirs[index] * d;
        spawnPosition.y = groundY;

        float yRot = SpawnYRotations[index % SpawnYRotations.Length];
        Quaternion rot = Quaternion.Euler(0f, yRot, 0f);

        GameObject enemy = Instantiate(prefabToSpawn, spawnPosition, rot);

        // 以 prefab 原本的大小為基準縮放，只做小幅變化，種類之間的體型差異才看得出來
        float scaleFactor = Random.Range(minScale, maxScale);
        enemy.transform.localScale = prefabToSpawn.transform.localScale * scaleFactor;

        EnemyController controller = enemy.GetComponent<EnemyController>();
        if (controller == null)
            controller = enemy.AddComponent<EnemyController>();

        controller.Init(placedObject.transform, type.speed * speedMultiplier, type.displayName);
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
            placedObject.transform.position = baseRestPosition;
    }

    private IEnumerator ShakeBase(Transform target)
    {
        float elapsed = 0f;
        while (elapsed < baseShakeDuration && target != null)
        {
            float strength = baseShakeAmount * (1f - elapsed / baseShakeDuration);
            Vector2 offset = Random.insideUnitCircle * strength;
            target.position = baseRestPosition + new Vector3(offset.x, 0f, offset.y);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (target != null)
            target.position = baseRestPosition;
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

    // 神碑正面持續轉向鏡頭
    private void Update()
    {
        if (aura != null)
            aura.transform.Rotate(0f, auraSpinSpeed * Time.deltaTime, 0f, Space.World);

        if (!isPlaced || placedObject == null || arCamera == null)
            return;

        Vector3 cameraPos = arCamera.transform.position;
        Vector3 dir = cameraPos - placedObject.transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(-dir, Vector3.up);
            placedObject.transform.rotation = Quaternion.Slerp(
                placedObject.transform.rotation,
                targetRot,
                Time.deltaTime * 5f
            );
        }
    }

    /// <summary>移除神碑、回到掃描平面的階段</summary>
    public void ResetPlacement()
    {
        StopSpawning();
        StopBaseShake();
        ClearEnemies();

        if (placedObject != null)
            Destroy(placedObject);
        placedObject = null;
        if (aura != null)
            Destroy(aura);
        aura = null;
        auraMaterial = null;

        isPlaced = false;
        gameRunning = false;
        pendingPlane = null;
        lastSpawnIndex = -1;

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
