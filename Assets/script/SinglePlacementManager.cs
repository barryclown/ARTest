using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;

public class SinglePlacementManager : MonoBehaviour
{
    public static SinglePlacementManager instance;

    [Header("Placement")]
    public GameObject placementPrefab;
    private GameObject placedObject;
    public float delaySeconds = 0.5f;

    [Header("Spawning")]
    public GameObject[] spawnPrefabs;
    public float spawnInterval;
    [Tooltip("怪物生成半徑")]
    public float SpawnRadius;

    [Header("Enemy Random Size")]
    [Tooltip("相對 prefab 的縮放倍率")]
    public float minScale;
    public float maxScale;

    [Header("Enemy Random Speed")]
    [Tooltip("敵人前進速度區間")]
    public float minSpeed;
    public float maxSpeed;

    private ARPlaneManager arPlaneManager;
    private Camera arCamera;
    private Coroutine spawnCoroutine;
    private bool isPlaced;
    private bool gameRunning;

    // 上一次使用的生成方向 (0~3)，避免連續同邊
    private int lastSpawnIndex = -1;

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

        if (minSpeed > maxSpeed)
        {
            float t = minSpeed;
            minSpeed = maxSpeed;
            maxSpeed = t;
        }
        if (maxSpeed <= 0f) maxSpeed = 0.1f;
    }

    private void Awake()
    {
        instance = this;
        arPlaneManager = FindObjectOfType<ARPlaneManager>();
        arCamera = Camera.main;
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

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (isPlaced || args.added.Count == 0)
            return;

        isPlaced = true;
        StartCoroutine(PlaceObjectWithDelay(args.added[0]));
    }

    private IEnumerator PlaceObjectWithDelay(ARPlane plane)
    {
        if (placementPrefab == null) yield break;

        Vector3 pos = plane.transform.position;
        Quaternion rot = plane.transform.rotation;

        yield return new WaitForSeconds(delaySeconds);

        placedObject = Instantiate(placementPrefab, pos, rot);
        placedObject.transform.localScale = new Vector3(0.45f, 0.45f, 0.1f);

        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = false;

            // 直接 Destroy 平面物件，避免重複 SetActive + Destroy
            foreach (var p in arPlaneManager.trackables)
            {
                Destroy(p.gameObject);
            }
        }

        gameRunning = true;
        UIManager.instance.StartGame();

        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (gameRunning && placedObject != null)
        {
            int count = Random.Range(1, 3);
            for (int i = 0; i < count; i++)
                SpawnRandomObject();

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private void SpawnRandomObject()
    {
        if (!gameRunning || placedObject == null ||
            spawnPrefabs == null || spawnPrefabs.Length == 0)
            return;

        int prefabIndex = Random.Range(0, spawnPrefabs.Length);
        GameObject prefabToSpawn = spawnPrefabs[prefabIndex];
        if (prefabToSpawn == null)
            return;

        Vector3 basePos = placedObject.transform.position;
        float d = SpawnRadius > 0f ? SpawnRadius : 5f;

        int dirCount = SpawnOffsetDirs.Length;
        if (dirCount == 0)
            return;

        int index;
        if (dirCount == 1)
        {
            index = 0;
        }
        else
        {
            do
            {
                index = Random.Range(0, dirCount);
            }
            while (index == lastSpawnIndex);
        }

        lastSpawnIndex = index;

        Vector3 spawnPosition = basePos + SpawnOffsetDirs[index] * d;
        spawnPosition.y = basePos.y;

        float yRot = SpawnYRotations[index % SpawnYRotations.Length];
        Quaternion rot = Quaternion.Euler(0f, yRot, 0f);

        GameObject enemy = Instantiate(prefabToSpawn, spawnPosition, rot);

        float scaleFactor = Random.Range(minScale, maxScale);
        enemy.transform.localScale = Vector3.one * scaleFactor;

        float finalSpeed = Random.Range(minSpeed, maxSpeed);

        EnemyController controller = enemy.GetComponent<EnemyController>();
        if (controller == null)
            controller = enemy.AddComponent<EnemyController>();

        controller.Init(placedObject.transform, finalSpeed);
    }

    public void OnBaseHit()
    {
        if (!gameRunning)
            return;

        bool isDead = UIManager.instance != null && UIManager.instance.DamageBase();
        if (isDead)
            LoseGame();
    }

    private void LoseGame()
    {
        if (!gameRunning)
            return;

        gameRunning = false;
        StopSpawning();

        if (UIManager.instance != null)
        {
            if (UIManager.instance.startPanel != null)
                UIManager.instance.startPanel.SetActive(false);

            if (UIManager.instance.losePanel != null)
                UIManager.instance.losePanel.SetActive(true);
        }

        Time.timeScale = 0f;
    }

    public void StopGame()
    {
        if (!gameRunning)
            return;

        gameRunning = false;
        StopSpawning();
    }

    private void StopSpawning()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    private void Update()
    {
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

    public void ResetPlacement()
    {
        if (placedObject != null)
            Destroy(placedObject);

        isPlaced = false;
        gameRunning = false;
        lastSpawnIndex = -1;

        if (arPlaneManager != null)
            arPlaneManager.enabled = true;

        StopSpawning();
    }
}
