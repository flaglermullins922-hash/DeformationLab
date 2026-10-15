// =============================================================================
// DeformationTools.cs —— 编辑器工具：一键重建场景 / 审计 / 构建 Windows 播放器
//
// 场景内容由 DeformationController.Awake 在运行时生成，因此场景文件里只需要
// 一个挂载 DeformationController 的空对象。菜单「全局变形作业」提供配套工具。
// =============================================================================

using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeformationLab.EditorTools
{
    public static class DeformationTools
    {
        const string ScenePath = "Assets/Scenes/DeformationLab.unity";
        const string BuildDir = "D:/un/DeformationLab_Build";
        const string ReportPath = "D:/un/DeformationLab/docs/构建报告.txt";

        [MenuItem("全局变形作业/1. 重建场景")]
        public static void RebuildScene() => RebuildSceneInternal();

        [MenuItem("全局变形作业/2. 审计当前场景")]
        public static void AuditScene() => AuditSceneInternal();

        [MenuItem("全局变形作业/3. 构建 Windows 播放器")]
        public static void BuildPlayer() => BuildPlayerInternal();

        // ---------------- 批处理入口 ----------------
        public static void RebuildSceneFromCLI() => RebuildSceneInternal();
        public static void BuildFromCLI() => BuildPlayerInternal();

        /// <summary>把自定义 shader 加入 Always Included Shaders，防止构建时被剥离。</summary>
        static void EnsureShadersIncluded()
        {
            AddAlwaysIncluded("DeformationLab/SurfaceLambert");
            AddAlwaysIncluded("DeformationLab/Unlit");
        }

        static void AddAlwaysIncluded(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("[DeformationTools] 找不到 shader: " + shaderName); return; }
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < prop.arraySize; i++)
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;   // 已包含
            prop.arraySize++;
            prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[DeformationTools] 已加入 Always Included Shaders: " + shaderName);
        }

        static void RebuildSceneInternal()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("DeformationLab");
            go.AddComponent<DeformationController>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log("[DeformationTools] 场景已重建并写入 Build Settings: " + ScenePath);
        }

        static void AuditSceneInternal()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DeformationTools] 场景审计");
            sb.AppendLine("场景文件存在: " + File.Exists(ScenePath));
            sb.AppendLine("Build Settings 场景数: " + EditorBuildSettings.scenes.Length);

            var cur = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sb.AppendLine("当前场景: " + cur.name + "  根对象数: " + cur.rootCount);
            bool hasBootstrap = false;
            foreach (var root in cur.GetRootGameObjects())
            {
                sb.AppendLine("  - " + root.name + "  组件: " + root.GetComponent<DeformationController>() != null ? "[有控制器]" : "[无]");
                if (root.GetComponent<DeformationController>() != null) hasBootstrap = true;
            }
            sb.AppendLine("Bootstrap(DeformationController) 存在: " + hasBootstrap);

            // 关键脚本编译引用检查
            sb.AppendLine("脚本类引用检查:");
            sb.AppendLine("  DeformationController: " + (typeof(DeformationController) != null ? "OK" : "MISSING"));
            sb.AppendLine("  DeformableModel: " + (typeof(DeformableModel) != null ? "OK" : "MISSING"));
            sb.AppendLine("  FfdLattice: " + (typeof(FfdLattice) != null ? "OK" : "MISSING"));
            sb.AppendLine("  DeformMath: " + (typeof(DeformMath) != null ? "OK" : "MISSING"));

            Debug.Log(sb.ToString());
            File.WriteAllText(ReportPath.Replace("构建报告.txt", "场景审计.txt"), sb.ToString(), Encoding.UTF8);
        }

        static void BuildPlayerInternal()
        {
            EnsureShadersIncluded();
            Directory.CreateDirectory(BuildDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildDir + "/DeformationLab.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);

            var sb = new StringBuilder();
            sb.AppendLine("[DeformationTools] 构建报告");
            sb.AppendLine("结果: " + report.summary.result);
            sb.AppendLine("错误: " + report.summary.totalErrors + "  警告: " + report.summary.totalWarnings);
            sb.AppendLine("大小: " + (report.summary.totalSize / (1024f * 1024f)).ToString("F1") + " MB");
            sb.AppendLine("输出: " + BuildDir + "/DeformationLab.exe");
            Debug.Log(sb.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
