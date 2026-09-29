using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 批次模式沒有鍵盤滑鼠，AR Foundation 的 XR Simulation 每幀讀 Keyboard.current 會丟 NullReferenceException。
/// 只在批次模式（自動測試）補一組虛擬裝置；一般進 Play 模式不受影響。
/// </summary>
static class BatchModeInputFix
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void AddVirtualDevices()
    {
        if (!Application.isBatchMode)
            return;

        if (Keyboard.current == null)
            InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null)
            InputSystem.AddDevice<Mouse>();
    }
}
