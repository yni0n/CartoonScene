// ============================================
// EnvironmentLayerAssigner.cs
// 用途: 编辑器工具 —— 为场景分配 Stencil 描边遮罩层（v2 回退版 + 帐篷单独修复）
// 分层规则:
//   角色=1(NotEqual) / 地面=2(NotEqual) / 封闭道具 colormap+M_Toon_Prop=3(Always)
//   开放几何 colormap_flat=4(NotEqual) / 树木 colormap_block=3(NotEqual)
//   帐篷专用: colormap_flat_tent-canvas=5(NotEqual)
//     —— 帐篷布独占层5，与土坡等层4物体异层 → 接触描边恢复；
//        帐篷木架不再被 flat 前缀误抓（前缀从 "tent" 收窄为 "tent-canvas"），
//        木架回归 colormap(Always) → 篷布上的内部结构线恢复
// 作者: Yang
// ============================================

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class EnvironmentLayerAssigner
{
    // 开放几何（单面布料/贴地平面片）：壳会整面糊在自己表面上，需 NotEqual 拦截
    // 注意: 只用 "tent-canvas" 不用 "tent"，避免把帐篷木架误抓进 flat
    private static readonly string[] FlatPrefixes =
        { "grass", "patch", "floor", "rock-flat", "tent-canvas", "structure", "bedroll", "blanket",
          "tarp", "canopy", "sail", "flag", "banner" };

    // 叠层几何（树木叶冠）：上层壳穿到下层叶面外形成描边碎片，需 NotEqual 拦截
    private static readonly string[] BlockedPrefixes = { "tree", "pine", "fir", "spruce", "bush", "leaves" };

    private const string ColormapPath = "Assets/Materials/Environment/colormap.mat";
    private const string FlatPath = "Assets/Materials/Environment/colormap_flat.mat";
    private const string BlockPath = "Assets/Materials/Environment/colormap_block.mat";
    private const string TentCanvasPath = "Assets/Materials/Environment/colormap_flat_tent-canvas.mat";
    private const string FlatDir = "Assets/Materials/Environment";

    [MenuItem("Tools/CartoonScene/Assign Environment Stencil Layers (Selected)")]
    public static void AssignSelected()
    {
        ApplyMaterialRefs();
        SwapRenderers();
        CleanupVariantLeftovers();
        AssetDatabase.SaveAssets();
        Debug.Log("Stencil 分层完成：角色=1 / 地面=2 / 封闭道具=3(Always) / 开放几何=4 / 树木=3(NE) / 帐篷布=5(NE)。");
    }

    // ---------- 材质层号与描边模式 ----------

    private static void ApplyMaterialRefs()
    {
        int count = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || !mat.HasProperty("_StencilRef")) continue;

            float layer;
            string n = mat.name;
            bool outlineAlways = false;

            if (n.StartsWith("M_Toon_Character")) layer = 1;            // 角色
            else if (n.Contains("Ground")) layer = 2;                   // 地面
            else if (n == "colormap" || n.StartsWith("M_Toon_Prop") || n == "colormap_block")
            {
                layer = 3;                                              // 场景道具
                outlineAlways = (n != "colormap_block");                // block 变体要拦截
            }
            else if (n == "colormap_flat") layer = 4;                   // 开放几何
            else if (n == "colormap_flat_tent-canvas") layer = 5;       // 帐篷布专用层
            else continue;

            mat.SetFloat("_StencilRef", layer);
            mat.SetFloat("_StencilWriteOp", 2f); // Replace：统一盖章
            if (mat.HasProperty("_OutlineStencilMode"))
                mat.SetFloat("_OutlineStencilMode", outlineAlways ? 8f : 6f); // Always=8 / NotEqual=6
            EditorUtility.SetDirty(mat);
            count++;
        }
        Debug.Log($"材质层号：{count} 个材质已写入（角色1 / 地面2 / 道具3 / flat4 / 帐篷布5）");
    }

    // ---------- 按模型名换材质变体 ----------

    private static void SwapRenderers()
    {
        Material flat = GetDerivedMaterial(FlatPath, 4f, 6f, "colormap_flat");
        Material block = GetDerivedMaterial(BlockPath, 3f, 6f, "colormap_block");
        Material tentCanvas = GetDerivedMaterial(TentCanvasPath, 5f, 6f, "colormap_flat_tent-canvas");
        if (flat == null || block == null || tentCanvas == null)
        {
            Debug.LogError($"找不到 {ColormapPath}，无法生成变体材质");
            return;
        }

        Material colormap = AssetDatabase.LoadAssetAtPath<Material>(ColormapPath);

        int flatCount = 0, blockCount = 0, restoredCount = 0, seen = 0;
        var flatNames = new List<string>();
        var nonColormapNames = new List<string>();
        foreach (GameObject root in Selection.gameObjects)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                Material current = renderer.sharedMaterial;
                if (current == null) continue;
                seen++;

                // 只处理 colormap 家族（共享 colormap + 各变体）；其他材质不动
                if (!current.name.StartsWith("colormap"))
                {
                    if (nonColormapNames.Count < 10) nonColormapNames.Add(renderer.gameObject.name);
                    continue;
                }

                string modelName = GetModelName(renderer.gameObject);
                if (IsPrefixModel(modelName, BlockedPrefixes))
                {
                    renderer.sharedMaterial = block;
                    EditorUtility.SetDirty(renderer);
                    blockCount++;
                }
                else if (modelName == "tent-canvas")
                {
                    renderer.sharedMaterial = tentCanvas;   // 帐篷布独占层5
                    EditorUtility.SetDirty(renderer);
                    flatCount++;
                    if (flatNames.Count < 20) flatNames.Add(modelName);
                }
                else if (IsPrefixModel(modelName, FlatPrefixes))
                {
                    renderer.sharedMaterial = flat;
                    EditorUtility.SetDirty(renderer);
                    flatCount++;
                    if (flatNames.Count < 20) flatNames.Add(modelName);
                }
                else if (!AssetDatabase.GetAssetPath(current).Equals(ColormapPath,
                         System.StringComparison.Ordinal))
                {
                    // 挂在变体上但不属于任何分类 → 恢复共享 colormap（清理历史误判）
                    renderer.sharedMaterial = colormap;
                    EditorUtility.SetDirty(renderer);
                    restoredCount++;
                }
                // 其余 = 共享 colormap（层3 Always）：封闭道具，保持不动
            }
        }
        Debug.Log($"诊断：共扫描 {seen} 个渲染器，flat {flatCount} 个（{string.Join(", ", flatNames)}），" +
                  $"block {blockCount} 个，恢复 colormap {restoredCount} 个。\n" +
                  $"非 colormap 材质的渲染器名示例: {string.Join(", ", nonColormapNames)}");
    }

    // 清理历史遗留的 flat 变体资产（帐篷布专用层除外）
    private static void CleanupVariantLeftovers()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { FlatDir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("colormap_flat_") && name != "colormap_flat_tent-canvas")
            {
                AssetDatabase.DeleteAsset(path);
                Debug.Log($"已清理历史变体材质: {name}");
            }
        }
    }

    // 实例名形如 "patch-grass (3)"，截掉克隆后缀得到模型名
    private static string GetModelName(GameObject go)
    {
        string name = go.name;
        int paren = name.IndexOf(" (");
        return paren >= 0 ? name.Substring(0, paren) : name;
    }

    private static bool IsPrefixModel(string modelName, string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (modelName.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // 从 colormap 派生变体材质（改层号与描边模式），存在则直接复用并刷新参数
    private static Material GetDerivedMaterial(string path, float layer, float stencilMode, string name)
    {
        Material derived = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (derived == null)
        {
            Material colormap = AssetDatabase.LoadAssetAtPath<Material>(ColormapPath);
            if (colormap == null) return null;

            derived = new Material(colormap); // 拷贝 shader 与全部属性（含 _BaseMap）
            derived.name = name;
            AssetDatabase.CreateAsset(derived, path);
            Debug.Log($"已生成 {path}（colormap 副本）");
        }

        derived.SetFloat("_StencilRef", layer);
        derived.SetFloat("_OutlineStencilMode", stencilMode);

        // 树木变体：开启顶点摆动（shader 端 SWAY_ON + 参数），每次重跑都刷新，改默认值也生效
        if (name == "colormap_block")
        {
            derived.SetFloat("_SwayEnabled", 1f);
            derived.SetFloat("_SwayStrength", 0.05f);
            derived.SetFloat("_SwaySpeed", 1.2f);
            derived.SetFloat("_SwayHeightScale", 0.3f);
        }
        EditorUtility.SetDirty(derived);
        return derived;
    }
}
