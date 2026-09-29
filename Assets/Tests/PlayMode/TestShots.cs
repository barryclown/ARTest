using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 自動測試的截圖工具：命令列帶 -shotDir &lt;資料夾&gt; 才會輸出，平常跑測試不產檔。
/// 把 Overlay 畫布暫時改成 Screen Space - Camera，連同 3D 場景一起渲染到 RenderTexture（批次模式沒有視窗也能出圖）。
/// </summary>
public static class TestShots
{
    public static readonly string Dir = GetArg("-shotDir");
    public static bool Enabled => !string.IsNullOrEmpty(Dir);

    public static IEnumerator Capture(string name, int width = 1080, int height = 1920)
    {
        if (!Enabled)
            yield break;

        Camera cam = Camera.main;
        var canvases = Object.FindObjectsOfType<Canvas>()
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
            .ToList();

        var rt = new RenderTexture(width, height, 24);
        RenderTexture prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        foreach (Canvas c in canvases)
        {
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 0.3f;
        }

        // 等一幀讓 CanvasScaler 依新的畫面大小重新排版
        yield return null;
        Canvas.ForceUpdateCanvases();
        cam.Render();

        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());

        foreach (Canvas c in canvases)
            c.renderMode = RenderMode.ScreenSpaceOverlay;
        cam.targetTexture = prevTarget;
        Object.Destroy(tex);
        rt.Release();
        Object.Destroy(rt);
    }

    static string GetArg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }
}
