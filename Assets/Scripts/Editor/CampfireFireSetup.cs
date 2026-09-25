using UnityEngine;
using UnityEditor;

// ============================================
// CampfireFireSetup.cs
// 用途: 选中篝火模型，一键生成粒子火焰（火焰主体 + 火星两层发射器）
//       自动生成柔光贴图 + Unlit 加法混合材质，全部资产可复用
// 使用: 选中篝火根物体 → Tools/CartoonScene/Setup Campfire Fire (Selected)
// 作者: Yang
// ============================================

public static class CampfireFireSetup
{
    private const string TexturePath = "Assets/Materials/Environment/T_FireSoftCircle.png";
    private const string FireMatPath = "Assets/Materials/Environment/M_Fire_Unlit.mat";
    private const string EmberMatPath = "Assets/Materials/Environment/M_Ember_Unlit.mat";

    [MenuItem("Tools/CartoonScene/Setup Campfire Fire (Selected)")]
    public static void Setup()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogError("[CampfireFireSetup] 请先在 Hierarchy 选中篝火物体。");
            return;
        }

        GameObject campfire = Selection.activeGameObject;
        Texture2D softCircle = GetOrCreateSoftCircle();
        Material fireMat = GetOrCreateMaterial(FireMatPath, softCircle, new Color(1f, 0.55f, 0.15f));
        Material emberMat = GetOrCreateMaterial(EmberMatPath, softCircle, new Color(1f, 0.85f, 0.5f));

        CreateEmitter(campfire, "Fire_Flames", fireMat, ember: false);
        CreateEmitter(campfire, "Fire_Embers", emberMat, ember: true);

        Debug.Log("[CampfireFireSetup] 火焰粒子已生成：Fire_Flames（火焰主体）+ Fire_Embers（火星）。" +
                  "位置不对就调整两个子物体的 Transform。");
    }

    // 一键移除火焰粒子及其生成的资产（用于放弃该效果或彻底清除报错源）
    [MenuItem("Tools/CartoonScene/Remove Campfire Fire (Selected)")]
    public static void Remove()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogError("[CampfireFireSetup] 请先在 Hierarchy 选中篝火物体。");
            return;
        }

        GameObject campfire = Selection.activeGameObject;
        int removed = 0;
        foreach (string childName in new[] { "Fire_Flames", "Fire_Embers" })
        {
            Transform child = campfire.transform.Find(childName);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
                removed++;
            }
        }

        // 清掉生成的资产，Project 里不留孤儿文件
        foreach (string path in new[] { FireMatPath, EmberMatPath, TexturePath })
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        Debug.Log($"[CampfireFireSetup] 已移除 {removed} 个火焰粒子物体及生成资产。");
    }

    // ---------------- 柔光贴图：径向渐变圆点，64x64，边缘透明 ----------------
    private static Texture2D GetOrCreateSoftCircle()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (existing != null) return existing;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 以中心为 1、边缘为 0 的平方衰减，做出柔和光点
                float dx = (x - size * 0.5f) / (size * 0.5f);
                float dy = (y - size * 0.5f) / (size * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        System.IO.File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(TexturePath);
        Object.DestroyImmediate(tex);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    // ---------------- Unlit 加法混合材质（URP Particles/Unlit） ----------------
    private static Material GetOrCreateMaterial(string path, Texture2D map, Color tint)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogError("[CampfireFireSetup] 找不到 URP Particles/Unlit shader，请确认 URP 包已安装。");
            return null;
        }

        mat = new Material(shader);
        mat.SetTexture("_BaseMap", map);
        mat.SetColor("_BaseColor", tint);
        // 加法混合：火焰是发光体，用 SrcAlpha/One 叠加到背景上
        mat.SetFloat("_Surface", 1f);                    // Transparent
        mat.SetFloat("_Blend", 2f);                      // Additive 预设
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ---------------- 粒子发射器 ----------------
    private static void CreateEmitter(GameObject parent, string name, Material mat, bool ember)
    {
        // 重跑不重复生成
        Transform old = parent.transform.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        // 火焰悬在木堆上方一点；火星起点更高（从火焰里冒出来）
        go.transform.localPosition = new Vector3(0f, ember ? 0.35f : 0.2f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = ember ? new ParticleSystem.MinMaxCurve(0.7f, 1.1f)
                                   : new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = ember ? new ParticleSystem.MinMaxCurve(0.8f, 1.4f)
                                : new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSize = ember ? new ParticleSystem.MinMaxCurve(0.03f, 0.07f)
                               : new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation3D = false;
        main.maxParticles = ember ? 24 : 40;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // 移动篝火时火星不跟着瞬移
        main.gravityModifier = ember ? 0.05f : 0f;

        var emission = ps.emission;
        emission.rateOverTime = ember ? 6f : 18f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = ember ? 0.05f : 0.15f;
        shape.angle = ember ? 12f : 8f;

        // 生命周期内：颜色从亮黄橙烧到暗红再透明；尺寸先胀后缩（火苗翻卷感）
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient g = new Gradient();
        if (ember)
        {
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f),
                        new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        }
        else
        {
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.4f), 0f),
                        new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.4f),
                        new GradientColorKey(new Color(0.6f, 0.1f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f),
                        new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        }
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(g);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = ember
            ? AnimationCurve.Linear(0f, 1f, 1f, 0.3f)
            : AnimationCurve.EaseInOut(0f, 0.3f, 1f, 0f);
        if (!ember) sizeCurve.AddKey(0.25f, 1f);   // 先快速胀大再收缩
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // 火星飘忽感：向上加速度 + 横向轻微扰动（XYZ 三轴必须同曲线模式）
        if (ember)
        {
            var velocityOffset = ps.velocityOverLifetime;
            velocityOffset.enabled = true;
            velocityOffset.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocityOffset.y = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            velocityOffset.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        }

        // 总粒子数预算：火焰 40 + 火星 24 ≈ 64，移动端安全
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = ember ? 5 : 0;   // 火星画在火焰前面
        renderer.alignment = ParticleSystemRenderSpace.View;
    }
}
