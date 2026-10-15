// =============================================================================
// DeformableModel.cs —— ★核心★ 可变形模型：加载 / 渲染 / 顶点数据访问
//
// 与 ObjViewer 的 ObjModel 思路一致：运行时自己解析 OBJ、自己建 Mesh，
// 因此 Mesh 天然可读写。本组件额外承担"变形基准"职责：
//   · restVertices —— 合并后的【初始顶点】数组，所有变形都从它出发计算，
//     所以反复切换变形 / 调参数【不会累积误差】
//   · partOffsets  —— 每个子网格在合并数组里的起始偏移
//   · SetVertices   —— 把变形结果写回所有子网格 Mesh 的总入口
//
// 支持两类来源：
//   · 导入 .obj（ObjParser，作业要求"能导入 obj 文件"）
//   · 内置程序化演示模型（细长圆柱 / 立方体 / 球体）
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DeformationLab
{
    /// <summary>一个子网格对应的运行时渲染资源。</summary>
    public sealed class MeshPart
    {
        public Mesh mesh;                 // 三角面 Mesh（可读写）
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Material material;
        public Color baseColor = Color.white;
        public Vector3[] restVertices;    // 本子网格的初始顶点
    }

    [DisallowMultipleComponent]
    public sealed class DeformableModel : MonoBehaviour
    {
        [Header("加载设置")]
        [Tooltip("启动时加载的内置模型：cylinder / box / sphere")]
        public string startModel = "cylinder";

        [Header("显示")]
        [Tooltip("模型材质主色（顶点色 × 此色；程序化模型自带高度渐变顶点色）")]
        public Color tint = Color.white;

        // ---------------------------------------------------------- 运行状态
        public ObjData Data { get; private set; }          // OBJ 解析结果（程序化模型时为 null）
        public MeshPart[] Parts { get; private set; } = new MeshPart[0];
        public Bounds ModelBounds { get; private set; }
        public bool IsLoaded { get; private set; }
        public string LoadedFrom { get; private set; }
        public string LoadError { get; private set; }
        public float LoadMilliseconds { get; private set; }

        /// <summary>合并后的初始顶点（变形基准），按 Parts 顺序拼接。</summary>
        public Vector3[] RestVertices { get; private set; } = new Vector3[0];

        /// <summary>每个子网格在 RestVertices 里的起始偏移。</summary>
        public int[] PartOffsets { get; private set; } = new int[0];

        public event Action<DeformableModel> Loaded;

        public int VertexCount => RestVertices.Length;
        public int SourceVertexCount => Data != null ? Data.positions.Count : 0;
        public int PartCount => Parts.Length;

        /// <summary>三角面总数（从各子网格 Mesh 统计）。</summary>
        public int TriangleCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Parts.Length; i++)
                    if (Parts[i].mesh != null) n += Parts[i].mesh.triangles.Length / 3;
                return n;
            }
        }

        static readonly Color[] Palette =
        {
            new Color(0.78f, 0.80f, 0.84f), new Color(0.55f, 0.58f, 0.63f),
            new Color(0.35f, 0.42f, 0.52f), new Color(0.72f, 0.62f, 0.48f),
            new Color(0.45f, 0.50f, 0.58f), new Color(0.62f, 0.66f, 0.72f),
            new Color(0.28f, 0.34f, 0.42f), new Color(0.85f, 0.72f, 0.50f),
        };

        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Start()
        {
            if (!IsLoaded) LoadProcedural(startModel);
        }

        // =====================================================================
        //  加载
        // =====================================================================

        /// <summary>导入 .obj 文件（绝对路径）。</summary>
        public void LoadFromFile(string absolutePath)
        {
            Clear();
            var sw = Stopwatch.StartNew();
            try
            {
                if (!File.Exists(absolutePath))
                    throw new FileNotFoundException("找不到模型文件: " + absolutePath);

                Data = ObjParser.ParseFile(absolutePath);
                Data.sourcePath = absolutePath;

                // 顺带读同名 .mtl
                string mtlPath = Path.ChangeExtension(absolutePath, ".mtl");
                if (File.Exists(mtlPath))
                    foreach (var m in ObjParser.ParseMtlFile(mtlPath)) Data.materials.Add(m);

                BuildPartsFromObj();
                FinishLoad(sw, absolutePath);
            }
            catch (Exception e)
            {
                sw.Stop();
                LoadError = e.Message;
                Debug.LogError("[DeformableModel] 加载失败: " + e);
            }
        }

        public void LoadFromText(string objText, string displayName = "inline.obj")
        {
            Clear();
            var sw = Stopwatch.StartNew();
            Data = ObjParser.Parse(objText, displayName);
            BuildPartsFromObj();
            FinishLoad(sw, displayName);
        }

        /// <summary>加载内置程序化演示模型。</summary>
        public void LoadProcedural(string type)
        {
            Clear();
            var sw = Stopwatch.StartNew();
            Mesh m;
            switch ((type ?? "cylinder").ToLowerInvariant())
            {
                case "box":      m = ProceduralMeshBuilder.BuildBox(2.2f, 5.5f, 2.2f, 40); break;
                case "sphere":   m = ProceduralMeshBuilder.BuildSphere(2.6f, 32, 20); break;
                default:         m = ProceduralMeshBuilder.BuildCylinder(1.2f, 6.0f, 24, 48); break;
            }
            AddPart(m, Color.white, "Procedural");
            FinishLoad(sw, "内置模型: " + type);
        }

        /// <summary>用解析结果构建所有子网格渲染对象。</summary>
        void BuildPartsFromObj()
        {
            Shader shader = GetSurfaceShader();
            for (int i = 0; i < Data.subMeshes.Count; i++)
            {
                ObjSubMesh sub = Data.subMeshes[i];
                int[] srcMap;
                Mesh mesh = ObjMeshBuilder.BuildSubMesh(Data, sub, out srcMap);

                ObjMaterialInfo info = Data.FindMaterial(sub.materialName);
                Color baseColor = info != null ? info.diffuse
                    : (Data.HasVertexColors ? Color.white : Palette[i % Palette.Length]);
                AddPart(mesh, baseColor, sub.objectName);
            }
        }

        void AddPart(Mesh mesh, Color baseColor, string name)
        {
            Shader shader = GetSurfaceShader();
            var part = new MeshPart { mesh = mesh, baseColor = baseColor };

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            part.filter = go.AddComponent<MeshFilter>();
            part.filter.sharedMesh = mesh;
            part.renderer = go.AddComponent<MeshRenderer>();
            part.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            part.material = new Material(shader) { name = "Mat_" + name };
            part.material.SetColor(ColorId, baseColor * tint);
            part.renderer.sharedMaterial = part.material;

            part.restVertices = mesh.vertices;
            Parts = ArrayUtil.Append(Parts, part);
        }

        static Shader GetSurfaceShader()
        {
            Shader s = Shader.Find("DeformationLab/SurfaceLambert");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        void FinishLoad(Stopwatch sw, string from)
        {
            sw.Stop();
            LoadMilliseconds = sw.ElapsedMilliseconds;
            LoadedFrom = from;
            LoadError = null;
            IsLoaded = true;

            // 合并初始顶点 + 记录偏移
            RestVertices = GetAllCurrentVertices();
            PartOffsets = new int[Parts.Length];
            int o = 0;
            for (int i = 0; i < Parts.Length; i++) { PartOffsets[i] = o; o += Parts[i].mesh.vertexCount; }

            // 包围盒
            Bounds b = new Bounds();
            bool has = false;
            for (int i = 0; i < Parts.Length; i++)
            {
                b.Encapsulate(Parts[i].mesh.bounds);
                has = true;
            }
            ModelBounds = has ? b : new Bounds(Vector3.zero, Vector3.one);

            Debug.Log(string.Format(
                "[DeformableModel] 加载完成: {0}  顶点={1}  三角面={2}  子网格={3}  耗时={4} ms",
                Path.GetFileName(from), VertexCount, TriangleCount, PartCount, LoadMilliseconds));

            try { Loaded?.Invoke(this); }
            catch (Exception cbEx) { Debug.LogError("[DeformableModel] Loaded 回调异常: " + cbEx); }
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            Parts = new MeshPart[0];
            RestVertices = new Vector3[0];
            PartOffsets = new int[0];
            Data = null;
            IsLoaded = false;
            LoadError = null;
        }

        // =====================================================================
        //  顶点访问 API
        // =====================================================================

        /// <summary>读当前全部顶点（合并成一个数组，供变形计算使用）。</summary>
        public Vector3[] GetAllCurrentVertices()
        {
            int total = 0;
            for (int i = 0; i < Parts.Length; i++) total += Parts[i].mesh.vertexCount;
            var all = new Vector3[total];
            int o = 0;
            for (int i = 0; i < Parts.Length; i++)
            {
                Vector3[] v = Parts[i].mesh.vertices;
                Array.Copy(v, 0, all, o, v.Length);
                o += v.Length;
            }
            return all;
        }

        /// <summary>把合并顶点数组写回所有子网格（变形结果的总入口）。</summary>
        public void SetVertices(Vector3[] all)
        {
            int o = 0;
            for (int i = 0; i < Parts.Length; i++)
            {
                MeshPart p = Parts[i];
                int n = p.mesh.vertexCount;
                if (o + n > all.Length) break;
                var slice = new Vector3[n];
                Array.Copy(all, o, slice, 0, n);
                p.mesh.SetVertices(slice);
                o += n;
            }
        }

        /// <summary>重算法线 + 包围盒（顶点被改后调用）。</summary>
        public void RecalculateNormalsAndBounds()
        {
            for (int i = 0; i < Parts.Length; i++)
            {
                Parts[i].mesh.RecalculateNormals();
                Parts[i].mesh.RecalculateBounds();
            }
        }

        /// <summary>恢复初始顶点（撤销所有变形）。</summary>
        public void RestoreRestPose()
        {
            SetVertices(RestVertices);
            RecalculateNormalsAndBounds();
        }

        /// <summary>把"初始顶点"重新设定为当前顶点（把某次变形固化为新基准）。</summary>
        public void CommitCurrentAsRest()
        {
            RestVertices = GetAllCurrentVertices();
            for (int i = 0; i < Parts.Length; i++)
                Parts[i].restVertices = (Vector3[])RestVertices.Clone();
        }
    }

    /// <summary>极简数组追加工具。</summary>
    public static class ArrayUtil
    {
        public static T[] Append<T>(T[] arr, T item)
        {
            if (arr == null) return new T[1] { item };
            var r = new T[arr.Length + 1];
            System.Array.Copy(arr, r, arr.Length);
            r[arr.Length] = item;
            return r;
        }
    }
}
