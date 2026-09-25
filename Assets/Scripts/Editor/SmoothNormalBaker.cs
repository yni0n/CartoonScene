using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ============================================
// SmoothNormalBaker.cs
// 用途: 编辑器工具 —— 为选中物体及其子物体的网格烘焙"按位置平均的平滑法线"，
//       写入 UV1（shader 侧 TEXCOORD1）通道，并另存为网格资产，使编辑模式与运行时表现一致
// 依赖: MeshFilter / SkinnedMeshRenderer（静态物体与蒙皮角色均支持）
// 作者: Yang
// ============================================
public static class SmoothNormalBaker
{
    private const string OutputFolder = "Assets/Art/Models/SmoothNormals";

    [MenuItem("Tools/CartoonScene/Bake Smooth Normals (Selected)")]
    private static void BakeSelected()
    {
        GameObject[] roots = Selection.gameObjects;
        if (roots.Length == 0)
        {
            Debug.LogWarning("[SmoothNormalBaker] 请先在 Hierarchy 中选中至少一个物体，再执行本命令。");
            return;
        }

        EnsureFolder();

        // 同一份源网格只烘焙一次（多个物体共用内置网格时直接复用同一份资产）
        Dictionary<Mesh, Mesh> bakedCache = new Dictionary<Mesh, Mesh>();
        List<MeshFilter> targets = new List<MeshFilter>();
        List<SkinnedMeshRenderer> skinnedTargets = new List<SkinnedMeshRenderer>();
        foreach (GameObject root in roots)
        {
            targets.AddRange(root.GetComponentsInChildren<MeshFilter>(true));
            skinnedTargets.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        int processed = 0;
        foreach (MeshFilter meshFilter in targets)
        {
            Mesh source = meshFilter.sharedMesh;
            if (source == null) continue;

            if (!bakedCache.TryGetValue(source, out Mesh baked))
            {
                baked = Bake(source);
                bakedCache.Add(source, baked);
            }

            if (baked != source)
            {
                meshFilter.sharedMesh = baked;
                processed++;
            }
        }

        // 蒙皮网格（角色）：取/回写都走 SkinnedMeshRenderer.sharedMesh
        // 烘焙副本会原样拷贝 boneWeights 与 bindposes，替换后蒙皮不受影响
        foreach (SkinnedMeshRenderer skinned in skinnedTargets)
        {
            Mesh source = skinned.sharedMesh;
            if (source == null) continue;

            if (!bakedCache.TryGetValue(source, out Mesh baked))
            {
                baked = Bake(source);
                bakedCache.Add(source, baked);
            }

            if (baked != source)
            {
                skinned.sharedMesh = baked;
                processed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        SceneView.RepaintAll();

        Debug.Log($"[SmoothNormalBaker] 完成：{processed} 个渲染器已换用烘焙后的网格（共 {bakedCache.Count} 份网格资产），平滑法线写入 UV1 通道。");
    }

    [MenuItem("Tools/CartoonScene/Clear Baked Smooth Normals In Selection")]
    private static void ClearSelected()
    {
        GameObject[] roots = Selection.gameObjects;
        if (roots.Length == 0)
        {
            Debug.LogWarning("[SmoothNormalBaker] 请先选中物体。");
            return;
        }

        int restored = 0;
        foreach (GameObject root in roots)
        {
            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh current = meshFilter.sharedMesh;
                if (current == null) continue;

                Mesh source = FindOriginalMesh(current);
                if (source == null) continue;

                meshFilter.sharedMesh = source;
                restored++;
            }

            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh current = skinned.sharedMesh;
                if (current == null) continue;

                Mesh source = FindOriginalMesh(current);
                if (source == null) continue;

                skinned.sharedMesh = source;
                restored++;
            }
        }

        AssetDatabase.Refresh();
        SceneView.RepaintAll();
        Debug.Log($"[SmoothNormalBaker] 已还原 {restored} 个渲染器的原始网格。");
    }

    // 按烘焙网格名定位原始网格：
    // 1) 旧约定：同目录同名独立资产（去掉 _SmoothNormal 后缀）
    // 2) 原始网格是 FBX 内部子资产（Kenney/角色均属此类）：
    //    扫描工程内所有模型文件，在其子资产中按网格名精确匹配
    private static Mesh FindOriginalMesh(Mesh baked)
    {
        const string suffix = "_SmoothNormal";
        if (!baked.name.EndsWith(suffix)) return null;
        string baseName = baked.name.Substring(0, baked.name.Length - suffix.Length);

        // 旧约定优先（独立 .asset 网格）
        string bakedPath = AssetDatabase.GetAssetPath(baked);
        if (!string.IsNullOrEmpty(bakedPath))
        {
            Mesh legacy = AssetDatabase.LoadAssetAtPath<Mesh>(bakedPath.Replace(suffix, ""));
            if (legacy != null) return legacy;
        }

        // FBX 子资产匹配：全工程模型文件扫一遍（编辑器菜单一次性操作，可接受）
        string[] modelGuids = AssetDatabase.FindAssets("t:Model");
        foreach (string guid in modelGuids)
        {
            string modelPath = AssetDatabase.GUIDToAssetPath(guid);
            foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
            {
                if (sub is Mesh m && m.name == baseName) return m;
            }
        }
        return null;
    }

    [MenuItem("Tools/CartoonScene/Clean Nested Baked Meshes")]
    private static void CleanNested()
    {
        // 全场景扫描（不依赖选中状态），避免遗漏渲染器导致资产删掉后出现 Missing
        MeshFilter[] allMeshFilters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        SkinnedMeshRenderer[] allSkinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);

        // 1. 把指向 xxx_SmoothNormal_SmoothNormal 的渲染器改回 xxx_SmoothNormal
        int relinked = 0;
        foreach (MeshFilter meshFilter in allMeshFilters)
        {
            Mesh current = meshFilter.sharedMesh;
            if (current == null) continue;

            Mesh fixedMesh = ResolveNestedMesh(current);
            if (fixedMesh == null) continue;

            meshFilter.sharedMesh = fixedMesh;
            relinked++;
        }
        foreach (SkinnedMeshRenderer skinned in allSkinned)
        {
            Mesh current = skinned.sharedMesh;
            if (current == null) continue;

            Mesh fixedMesh = ResolveNestedMesh(current);
            if (fixedMesh == null) continue;

            skinned.sharedMesh = fixedMesh;
            relinked++;
        }

        // 2. 删除所有嵌套副本资产
        int removed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { OutputFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileNameWithoutExtension(path).EndsWith("_SmoothNormal_SmoothNormal")) continue;

            AssetDatabase.DeleteAsset(path);
            removed++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        SceneView.RepaintAll();

        Debug.Log($"[SmoothNormalBaker] 已重指向 {relinked} 个渲染器，删除 {removed} 份嵌套副本资产。");
    }

    // ---------- 内部实现 ----------

    // 若网格指向嵌套副本资产（xxx_SmoothNormal_SmoothNormal），返回应指向的正确资产；否则返回 null
    private static Mesh ResolveNestedMesh(Mesh current)
    {
        string path = AssetDatabase.GetAssetPath(current);
        if (string.IsNullOrEmpty(path) || !path.Contains("_SmoothNormal_SmoothNormal")) return null;

        string fixedPath = path.Replace("_SmoothNormal_SmoothNormal", "_SmoothNormal");
        return AssetDatabase.LoadAssetAtPath<Mesh>(fixedPath);
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(OutputFolder)) return;

        Directory.CreateDirectory(OutputFolder);
        AssetDatabase.Refresh();
    }

    private static Mesh Bake(Mesh source)
    {
        // 已经是烘焙产物就直接跳过，避免二次烘焙产生 xxx_SmoothNormal_SmoothNormal 这类嵌套副本
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (!string.IsNullOrEmpty(sourcePath) && sourcePath.StartsWith(OutputFolder))
        {
            return source;
        }

        string assetPath = $"{OutputFolder}/{source.name}_SmoothNormal.asset";

        // 已经烘焙过就直接复用，保证本命令可重复执行而不产生副本堆积
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null) return existing;

        Mesh copy = Object.Instantiate(source);
        copy.name = $"{source.name}_SmoothNormal";

        Vector3[] vertices = copy.vertices;
        Vector3[] normals = copy.normals;
        if (normals == null || normals.Length != vertices.Length || vertices.Length == 0)
        {
            Debug.LogWarning($"[SmoothNormalBaker] 网格 {source.name} 缺少法线数据，已跳过。");
            Object.DestroyImmediate(copy);
            return source;
        }

        // 1. 按顶点位置分组：位置完全相同 = 硬边拆分出来的同一个物理角落
        //    注意：这里用坐标精确值作键，适用于图元与硬边模型；
        //    若模型顶点带浮点误差，应先把坐标量化（如 round(x * 1000)）再作键
        Dictionary<Vector3, List<int>> groups = new Dictionary<Vector3, List<int>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!groups.TryGetValue(vertices[i], out List<int> list))
            {
                list = new List<int>();
                groups.Add(vertices[i], list);
            }
            list.Add(i);
        }

        // 2. 每组法线取平均，得到"焊住"转角的平滑法线
        Vector3[] smoothNormals = new Vector3[vertices.Length];
        foreach (List<int> group in groups.Values)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < group.Count; i++)
            {
                sum += normals[group[i]];
            }

            if (sum.sqrMagnitude < 1e-8f)
            {
                // 对向法线相互抵消（如薄片模型），退回组内第一个顶点的法线
                sum = normals[group[0]];
            }
            else
            {
                sum.Normalize();
            }

            for (int i = 0; i < group.Count; i++)
            {
                smoothNormals[group[i]] = sum;
            }
        }

        // 3. 写入 UV1 通道（Shader 侧以 : TEXCOORD1 读取）
        copy.SetUVs(1, new List<Vector3>(smoothNormals));

        AssetDatabase.CreateAsset(copy, assetPath);
        return copy;
    }
}
