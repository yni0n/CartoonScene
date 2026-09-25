using UnityEngine;

// ============================================
// LightFlicker.cs
// 用途: 篝火/灯笼点光源的火焰闪烁效果，Perlin 噪声 + 双频正弦混合
// 挂载: 直接挂在需要闪烁的 Light 组件所在 GameObject 上
// 作者: Yang
// ============================================

[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [Header("基准与波动")]
    [SerializeField] private float baseIntensity = 2f;
    [SerializeField] private float flickerAmount = 0.4f;   // 波动幅度（±）

    [Header("频率")]
    [SerializeField] private float slowSpeed = 1.7f;       // 慢频：火苗整体起伏
    [SerializeField] private float fastSpeed = 6.3f;       // 快频：细碎跳动

    [Header("色彩微闪")]
    [SerializeField] private float colorShift = 0.06f;     // 色温微移幅度，0 关闭

    private Light targetLight;
    private Color baseColor;
    private float noiseSeed;

    private void Awake()
    {
        targetLight = GetComponent<Light>();
        baseColor = targetLight.color;
        // 每盏灯随机种子，多盏灯不会同步闪烁
        noiseSeed = Random.value * 100f;
    }

    private void Update()
    {
        // Perlin 噪声提供自然的随机起伏，叠加快慢两个正弦避免机械感
        float noise = Mathf.PerlinNoise(noiseSeed, Time.time * slowSpeed);
        float wave = Mathf.Sin(Time.time * slowSpeed) * 0.5f
                   + Mathf.Sin(Time.time * fastSpeed) * 0.25f;

        float intensity = baseIntensity + (noise - 0.5f) * 2f * flickerAmount
                                        + wave * flickerAmount * 0.5f;
        targetLight.intensity = Mathf.Max(0f, intensity);

        // 火苗猛跳时颜色微微偏白，安静时偏暗橙
        if (colorShift > 0f)
        {
            float heat = Mathf.Clamp01((noise - 0.5f) * 2f);
            targetLight.color = Color.Lerp(baseColor * (1f - colorShift),
                                           Color.Lerp(baseColor, Color.white, colorShift),
                                           heat);
        }
    }
}
