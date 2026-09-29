using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 出 Android APK。
// 編輯器：Tools/AR Base/Build Android APK（輸出到專案的 Builds/）
// 批次模式：Unity.exe -batchmode -quit -projectPath <專案> -buildTarget Android
//           -executeMethod BuildTools.BuildAndroidCli -buildOutput <apk 路徑> [-cleanBuild]
// -cleanBuild：清掉 Gradle 等建置快取。改過 AR 需求這類 manifest 設定時一定要加，
//              ARCore 外掛只會往快取的 manifest「補」標籤、不會移除舊的。
public static class BuildTools
{
    const string DefaultOutput = "Builds/GuardianStele.apk";

    [MenuItem("Tools/AR Base/Build Android APK")]
    public static void BuildAndroidMenu()
    {
        BuildAndroid(DefaultOutput);
    }

    public static void BuildAndroidCli()
    {
        string output = GetArg("-buildOutput") ?? DefaultOutput;
        bool clean = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-cleanBuild") >= 0;
        bool ok = BuildAndroid(output, clean);
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool BuildAndroid(string output, bool clean = false)
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildTools] Build Settings 沒有場景");
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        EditorUserBuildSettings.buildAppBundle = false;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = clean ? BuildOptions.CleanBuildCache : BuildOptions.None,
        });

        BuildSummary s = report.summary;
        Debug.Log($"[BuildTools] {s.result} → {output}（{s.totalSize / (1024f * 1024f):F1} MB，錯誤 {s.totalErrors}，{s.totalTime.TotalSeconds:F0} 秒）");
        return s.result == BuildResult.Succeeded;
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
