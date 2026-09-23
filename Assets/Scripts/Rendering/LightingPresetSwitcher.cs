using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ============================================
// LightingPresetSwitcher.cs
// 用途: 在多种光照环境预设之间切换（主光 + 环境光 + 天空背景 + 后处理 Profile）
// 依赖: UnityEngine.InputSystem（本项目使用新版输入系统）
// 作者: Yang
// ============================================

[System.Serializable]
public struct LightingPreset
{
    public string presetName;

    [Header("主光")]
    public Color sunColor;
    [Range(0f, 4f)] public float sunIntensity;
    public Vector3 sunEulerAngles;

    [Header("环境")]
    public Color ambientColor;
    public Color skyColor;

    [Header("后处理")]
    public VolumeProfile postProfile;
}

public class LightingPresetSwitcher : MonoBehaviour
{
    [SerializeField] private Light sunLight;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Volume globalVolume;
    [SerializeField] private LightingPreset[] presets;
    [SerializeField] private int startIndex;
    [SerializeField] private Key nextPresetKey = Key.N;

    private int currentIndex = -1;

    private void Start()
    {
        if (presets == null || presets.Length == 0)
        {
            Debug.LogWarning("[LightingPresetSwitcher] 未配置任何光照预设。");
            enabled = false;
            return;
        }

        Apply(Mathf.Clamp(startIndex, 0, presets.Length - 1));
    }

    private void Update()
    {
        if (nextPresetKey == Key.None || Keyboard.current == null) return;
        if (Keyboard.current[nextPresetKey].wasPressedThisFrame) NextPreset();
    }

    public void NextPreset()
    {
        Apply((currentIndex + 1) % presets.Length);
    }

    public void Apply(int index)
    {
        if (presets == null || presets.Length == 0) return;

        currentIndex = Mathf.Clamp(index, 0, presets.Length - 1);
        LightingPreset preset = presets[currentIndex];

        // 1. 主光：颜色 / 强度 / 方向
        if (sunLight != null)
        {
            sunLight.color = preset.sunColor;
            sunLight.intensity = preset.sunIntensity;
            sunLight.transform.rotation = Quaternion.Euler(preset.sunEulerAngles);
        }

        // 2. 环境光：在 URP 里这就是阴影区的颜色，直接决定暗部色相
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = preset.ambientColor;

        // 3. 天空背景色（三套环境差异最直观的来源）
        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = preset.skyColor;
        }

        // 4. 后处理：整体换 Profile，风格随环境统一切换
        if (globalVolume != null && preset.postProfile != null)
        {
            globalVolume.sharedProfile = preset.postProfile;
        }

        // 5. 让环境光/探针立即刷新，避免沿用上一帧的值
        DynamicGI.UpdateEnvironment();

        Debug.Log($"[LightingPresetSwitcher] 切换到：{preset.presetName}");
    }
}
