using UnityEngine;
using UnityEditor;

// ============================================
// CameraBookmarks.cs
// 用途: 主相机机位收藏夹——保存/恢复 Main Camera 的位姿
// 使用: Tools/CartoonScene/Camera Bookmarks 打开窗口
//       1. 先把 Main Camera 摆好（或用 Ctrl+Shift+F 对齐 Scene 视图）
//       2. 点 Save Current Pose 收藏当前机位
//       3. 随时点 Apply 跳回该机位
// 数据持久化: EditorPrefs（关 Unity 也不丢，按场景名隔离）
// 作者: Yang
// ============================================

public class CameraBookmarks : EditorWindow
{
    private class Bookmark
    {
        public string name;
        public Vector3 position;
        public Quaternion rotation;
    }

    private const string PrefKeyPrefix = "CameraBookmarks_";

    private Vector3 scrollPos;

    [MenuItem("Tools/CartoonScene/Camera Bookmarks")]
    public static void Open()
    {
        GetWindow<CameraBookmarks>("Camera Bookmarks");
    }

    private string SceneKey => PrefKeyPrefix + EditorSceneManagerProxy();

    private static string EditorSceneManagerProxy()
    {
        // 用场景路径做 key，多场景各自的机位互不干扰
        string path = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        return string.IsNullOrEmpty(path) ? "Untitled" : path.GetHashCode().ToString();
    }

    private System.Collections.Generic.List<Bookmark> Load()
    {
        var list = new System.Collections.Generic.List<Bookmark>();
        string json = EditorPrefs.GetString(SceneKey, "");
        if (!string.IsNullOrEmpty(json))
            list = NewtonsoftLite.DeserializeList(json);
        return list;
    }

    private void Save(System.Collections.Generic.List<Bookmark> list)
    {
        EditorPrefs.SetString(SceneKey, NewtonsoftLite.SerializeList(list));
    }

    private void OnGUI()
    {
        var list = Load();

        EditorGUILayout.HelpBox(
            "先摆好 Main Camera（Scene 视图对好角度后选中相机按 Ctrl+Shift+F），再点保存。", MessageType.Info);

        if (GUILayout.Button("Save Current Pose（收藏当前主相机机位）"))
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[CameraBookmarks] 场景里没有标记为 MainCamera 的相机。");
                return;
            }
            list.Add(new Bookmark
            {
                name = $"机位 {list.Count + 1}",
                position = cam.transform.position,
                rotation = cam.transform.rotation
            });
            Save(list);
        }

        EditorGUILayout.Space();
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        for (int i = list.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal("box");

            list[i].name = EditorGUILayout.TextField(list[i].name, GUILayout.MinWidth(80));

            if (GUILayout.Button("Apply", GUILayout.Width(60)))
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Undo.RecordObject(cam.transform, "Apply Camera Bookmark");
                    cam.transform.SetPositionAndRotation(list[i].position, list[i].rotation);
                }
            }

            if (GUILayout.Button("Delete", GUILayout.Width(60)))
            {
                list.RemoveAt(i);
                Save(list);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        if (GUI.changed) Save(list);
    }

    // 轻量 JSON 序列化（避免依赖 Newtonsoft 包是否安装）
    private static class NewtonsoftLite
    {
        public static string SerializeList(System.Collections.Generic.List<Bookmark> list)
        {
            var items = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                items[i] = $"{Esc(list[i].name)}|{list[i].position.x},{list[i].position.y},{list[i].position.z}|{list[i].rotation.x},{list[i].rotation.y},{list[i].rotation.z},{list[i].rotation.w}";
            return string.Join(";", items);
        }

        public static System.Collections.Generic.List<Bookmark> DeserializeList(string json)
        {
            var list = new System.Collections.Generic.List<Bookmark>();
            foreach (string item in json.Split(';'))
            {
                string[] parts = item.Split('|');
                if (parts.Length != 3) continue;
                float[] p = Parse(parts[1]);
                float[] r = Parse(parts[2]);
                if (p == null || r == null) continue;
                list.Add(new Bookmark
                {
                    name = Unesc(parts[0]),
                    position = new Vector3(p[0], p[1], p[2]),
                    rotation = new Quaternion(r[0], r[1], r[2], r[3])
                });
            }
            return list;
        }

        private static string Esc(string s) => s.Replace("|", "／").Replace(";", "；").Replace(",", "，");
        private static string Unesc(string s) => s; // 保存的名字里换过逗号，读回不还原也无碍显示
        private static float[] Parse(string s)
        {
            string[] arr = s.Split(',');
            if (arr.Length < 3) return null;
            var result = new float[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                if (!float.TryParse(arr[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out result[i]))
                    return null;
            return result;
        }
    }
}
