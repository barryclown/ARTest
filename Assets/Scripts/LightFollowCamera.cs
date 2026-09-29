using UnityEngine;

/// <summary>
/// 神碑永遠轉向玩家，但場景主光方向固定，玩家走到背光面時碑面會一片黑。
/// 讓主光跟著鏡頭的水平朝向，從玩家斜後上方打過去，正對玩家的那一面永遠有光。
/// </summary>
[RequireComponent(typeof(Light))]
public class LightFollowCamera : MonoBehaviour
{
    [Tooltip("光線往下打的角度")]
    public float pitch = 45f;
    [Tooltip("相對鏡頭朝向往右偏幾度，讓碑面有明暗層次")]
    public float yawOffset = 35f;

    private Camera cam;

    private void LateUpdate()
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return;

        transform.rotation = Quaternion.Euler(pitch, cam.transform.eulerAngles.y + yawOffset, 0f);
    }
}
