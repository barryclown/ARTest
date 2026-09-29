using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

/// <summary>
/// 自動測試與錄影共用的舞台：關掉 XR（沒有真的相機畫面），鏡頭改由測試擺位，
/// 放一塊碎石地面代替真實地板，視角調成接近手機直拿的相機。
/// </summary>
public static class TestStage
{
    // 鏡頭站在斜 45 度，四個生怪方向都會從畫面兩側往神碑聚集，不會有龜從鏡頭腳下冒出來
    public static readonly Vector3 CameraPosition = new Vector3(-2.7f, 1.8f, 0.3f);
    public static readonly Vector3 BasePosition = new Vector3(0f, 0f, 3f);

    public static void ShutdownXR()
    {
        // XR Simulation 在重載場景後會抓著已刪除的鏡頭報錯；測試只驗遊戲邏輯
        XRManagerSettings xr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (xr != null && xr.isInitializationComplete)
        {
            xr.StopSubsystems();
            xr.DeinitializeLoader();
        }
    }

    public static Camera Prepare()
    {
        foreach (ARPlaneManager m in Object.FindObjectsOfType<ARPlaneManager>())
            m.enabled = false;

        Camera cam = Camera.main;
        foreach (Behaviour b in cam.GetComponents<Behaviour>())
        {
            if (b.GetType().Name == "TrackedPoseDriver" || b is ARCameraBackground)
                b.enabled = false;
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
        cam.fieldOfView = 70f;   // 手機直拿時主鏡頭的垂直視角大約 65～75 度
        cam.transform.position = CameraPosition;
        cam.transform.LookAt(BasePosition + Vector3.up * 0.35f);

        // 遠處淡進背景色，地面邊緣不會變成一條硬地平線
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = cam.backgroundColor;
        RenderSettings.fogStartDistance = 5f;
        RenderSettings.fogEndDistance = 14f;

        CreateGround();
        return cam;
    }

    static void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "TestGround";
        Object.Destroy(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(BasePosition.x, BasePosition.y - 0.005f, BasePosition.z);
        ground.transform.localScale = Vector3.one * 4f;

        var r = ground.GetComponent<Renderer>();
#if UNITY_EDITOR
        var gravel = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Models/Materials/Materials/Gravel040_1K-PNG_Color.mat");
        if (gravel != null)
        {
            r.material = new Material(gravel);
            r.material.mainTextureScale = new Vector2(24f, 24f);
            r.material.color = new Color(0.7f, 0.68f, 0.64f);
            return;
        }
#endif
        r.material.color = new Color(0.35f, 0.34f, 0.32f);
    }
}
