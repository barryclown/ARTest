using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 手機不支援 ARCore（或裝不起來）時改用模擬場景：虛擬地面＋陀螺儀（沒有就用手指拖曳）看四周，神碑放在正前方。
/// 支援 AR 的手機照常走 AR 流程；編輯器裡用 XR Simulation，不會切換。
/// </summary>
public class NonARFallback : MonoBehaviour
{
    public const string NoticeText = "此裝置不支援 AR，改用模擬場景";
    const string InstallTriedKey = "arcore_install_tried";

    [Tooltip("檢查 AR 可用性最多等幾秒")]
    public float checkTimeout = 8f;
    [Tooltip("模擬地面的材質")]
    public Material groundMaterial;
    public Color backgroundColor = new Color(0.12f, 0.15f, 0.19f);
    [Tooltip("神碑放在鏡頭前方幾公尺")]
    public float baseDistance = 3f;
    [Tooltip("鏡頭高度（公尺）")]
    public float cameraHeight = 1.4f;

    public static bool Active { get; private set; }

    private ARSession session;
    private Camera cam;
    private bool useGyro;
    private bool gyroCalibrated;
    private Quaternion headingOffset = Quaternion.identity;
    private float yaw;
    private float pitch = 18f;
    private float dragDistance;

    private IEnumerator Start()
    {
        Active = false;
        if (Application.isEditor)
            yield break;

        session = FindObjectOfType<ARSession>();
        yield return CheckAR();
    }

    private IEnumerator CheckAR()
    {
        float end = Time.realtimeSinceStartup + checkTimeout;
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
        {
            var check = ARSession.CheckAvailability();
            while (check.MoveNext() && Time.realtimeSinceStartup < end)
                yield return check.Current;
        }

        // 支援但還沒裝：只請 Play 商店裝一次；裝過失敗（例如 Play 商店找不到適用版本）就不再跳視窗
        if (ARSession.state == ARSessionState.NeedsInstall && PlayerPrefs.GetInt(InstallTriedKey, 0) == 0)
        {
            PlayerPrefs.SetInt(InstallTriedKey, 1);
            PlayerPrefs.Save();
            yield return ARSession.Install();
        }

        ARSessionState s = ARSession.state;
        if (s == ARSessionState.Unsupported || s == ARSessionState.NeedsInstall || s == ARSessionState.None ||
            s == ARSessionState.CheckingAvailability)
        {
            Activate();
        }
        else
        {
            PlayerPrefs.SetInt(InstallTriedKey, 0);
            // 剛從 Play 商店裝好 ARCore 時，AR session 要重新啟動才會開始追蹤
            if (session != null)
            {
                session.enabled = false;
                session.enabled = true;
            }
        }
    }

    /// <summary>切到模擬場景（不支援 AR 時自動呼叫；測試也會直接呼叫）</summary>
    public void Activate()
    {
        if (Active)
            return;
        Active = true;

        if (session == null)
            session = FindObjectOfType<ARSession>();
        if (session != null)
            session.enabled = false;
        foreach (ARPlaneManager m in FindObjectsOfType<ARPlaneManager>())
            m.enabled = false;

        cam = Camera.main;
        if (cam != null)
        {
            foreach (Behaviour b in cam.GetComponents<Behaviour>())
            {
                if (b is ARCameraBackground || b is ARCameraManager || b.GetType().Name == "TrackedPoseDriver")
                    b.enabled = false;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            cam.transform.position = new Vector3(0f, cameraHeight, 0f);
            cam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = backgroundColor;
        RenderSettings.fogStartDistance = 6f;
        RenderSettings.fogEndDistance = 16f;
        CreateGround();

        useGyro = SystemInfo.supportsGyroscope;
        if (useGyro)
            Input.gyro.enabled = true;

        if (UIManager.instance != null)
            UIManager.instance.ShowFallbackNotice(NoticeText);
        if (SinglePlacementManager.instance != null)
            SinglePlacementManager.instance.UseVirtualGround(new Vector3(0f, 0f, baseDistance));
    }

    private void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "FallbackGround";
        Destroy(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(0f, -0.005f, baseDistance);
        ground.transform.localScale = Vector3.one * 4f;
        if (groundMaterial != null)
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
    }

    private void Update()
    {
        if (!Active || cam == null)
            return;

        if (useGyro)
        {
            // 陀螺儀姿態轉成 Unity 座標；第一次拿到有效數值時，把當下朝向對準神碑（+Z）
            Quaternion q = Input.gyro.attitude;
            if (q == Quaternion.identity)
                return;
            Quaternion device = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(q.x, q.y, -q.z, -q.w);
            if (!gyroCalibrated)
            {
                headingOffset = Quaternion.Euler(0f, -device.eulerAngles.y, 0f);
                gyroCalibrated = true;
            }
            cam.transform.rotation = headingOffset * device;
            return;
        }

        // 沒有陀螺儀：單指拖曳轉視角；移動很小的點擊交給萬年龜的點擊判定
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
                dragDistance = 0f;
            else if (t.phase == TouchPhase.Moved)
            {
                dragDistance += t.deltaPosition.magnitude;
                if (dragDistance > 12f)
                {
                    yaw += t.deltaPosition.x * 0.15f;
                    pitch = Mathf.Clamp(pitch - t.deltaPosition.y * 0.1f, -10f, 60f);
                    cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                }
            }
        }
    }

    private void OnDestroy()
    {
        Active = false;
    }
}
