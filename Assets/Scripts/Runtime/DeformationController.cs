// =============================================================================
// DeformationController.cs —— ★大脑★ 全局变形演示的主控制器
//
// 职责：
//   1. Awake 里搭好整个场景（相机/灯光/地面/模型/UI/交互）——零 Inspector 拖拽
//   2. 管理变形模式（锥化/扭曲/弯曲/FFD/重置）与各项参数
//   3. 从 restVertices 出发计算变形结果并写回 Mesh（不累积误差）
//   4. 构建 / 更新 / 重置 FFD 格子及其可视化
//
// 所有变形都作用在【模型局部空间】（模型 GameObject 位于原点），包围盒取
// model.ModelBounds，因此变形的"主轴/方向"直观可预测。
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeformationLab
{
    /// <summary>变形模式。</summary>
    public enum DeformMode { None = 0, Taper = 1, Twist = 2, Bend = 3, FFD = 4 }

    public sealed class DeformationController : MonoBehaviour
    {
        [Header("引用（Awake 自动装配）")]
        [NonSerialized] public DeformableModel model;
        [NonSerialized] public Camera cam;
        [NonSerialized] public DeformationUI ui;
        [NonSerialized] public DeformationInteraction interaction;

        [Header("变形参数")]
        public DeformMode mode = DeformMode.None;
        public DeformAxis axis = DeformAxis.Y;       // 主轴
        public DeformAxis bendDir = DeformAxis.X;    // 弯曲方向（垂直于主轴）
        [Range(-1f, 1f)]  public float taperAmount = -0.6f;   // 锥化系数 k
        [Range(0f, 360f)] public float twistDeg = 180f;       // 扭转角（度）
        [Range(0f, 180f)] public float bendDeg = 90f;         // 弯曲角（度）
        [Range(0f, 1f)]   public float bendCenter = 0.5f;     // 弯曲中心（沿主轴）
        public int ffdResolution = 4;                          // FFD 格子分辨率 2/3/4

        // ---------------------------------------------------------- FFD 状态
        [NonSerialized] public FfdLattice ffd;
        [NonSerialized] public Transform ffdRoot;
        [NonSerialized] public List<GameObject> ffdHandles;
        [NonSerialized] public Mesh ffdWireMesh;
        [NonSerialized] public MeshFilter ffdWireFilter;
        [NonSerialized] public Vector3[] embeddedParams;   // 每个顶点的 (s,t,u)
        [NonSerialized] public bool ffdActive;

        void Awake()
        {
            BuildScene();
        }

        // =====================================================================
        //  场景搭建
        // =====================================================================
        void BuildScene()
        {
            cam = DeformationFactory.BuildEnvironment();

            var modelGo = new GameObject("Model");
            model = modelGo.AddComponent<DeformableModel>();
            model.Loaded += OnModelLoaded;

            // UI 与交互（在加载模型之前装配好，加载完成回调里才用得到它们）
            ui = gameObject.AddComponent<DeformationUI>();
            interaction = gameObject.AddComponent<DeformationInteraction>();
            ui.controller = this;
            interaction.controller = this;
            interaction.cam = cam;

            // 开发辅助：命令行自动截图（不带 -shot 参数时无感）
            gameObject.AddComponent<DevAutoShot>();

            // 默认加载细长圆柱（变形最直观）
            model.LoadProcedural("cylinder");
        }

        void OnModelLoaded(DeformableModel m)
        {
            if (interaction != null) interaction.FrameModel();

            // 新模型从初始姿态开始
            mode = DeformMode.None;
            ClearFfdVisual();
            ffd = null;
            embeddedParams = null;

            if (ui != null) { ui.Refresh(); ui.RefreshMode(); }
        }

        // =====================================================================
        //  模式 / 参数入口（UI 与快捷键都走这里）
        // =====================================================================
        public void SetMode(DeformMode m)
        {
            mode = m;
            if (m == DeformMode.FFD && model != null && model.IsLoaded)
                BuildFfd();
            else
            {
                ClearFfdVisual();
                ApplyDeformation();
            }
            if (ui != null) ui.RefreshMode();
        }

        public void SetAxis(DeformAxis a)
        {
            axis = a;
            if (a == bendDir) bendDir = (a == DeformAxis.X) ? DeformAxis.Z : DeformAxis.X; // 保证垂直
            if (mode == DeformMode.FFD) BuildFfd();
            else ApplyDeformation();
            if (ui != null) ui.RefreshMode();
        }

        public void SetBendDir(DeformAxis d)
        {
            if (d == axis) return;
            bendDir = d;
            ApplyDeformation();
            if (ui != null) ui.RefreshMode();
        }

        public void SetTaper(float k)    { taperAmount = k;    if (mode == DeformMode.Taper) ApplyDeformation(); }
        public void SetTwist(float deg)  { twistDeg = deg;     if (mode == DeformMode.Twist) ApplyDeformation(); }
        public void SetBend(float deg)   { bendDeg = deg;      if (mode == DeformMode.Bend)  ApplyDeformation(); }
        public void SetBendCenter(float c) { bendCenter = c;   if (mode == DeformMode.Bend)  ApplyDeformation(); }

        public void SetFfdResolution(int n)
        {
            ffdResolution = Mathf.Clamp(n, 2, 4);
            if (mode == DeformMode.FFD) BuildFfd();
        }

        // =====================================================================
        //  变形应用
        // =====================================================================
        /// <summary>从 restVertices 出发，按当前模式 + 参数计算新顶点并写回。</summary>
        public void ApplyDeformation()
        {
            if (model == null || !model.IsLoaded) return;

            if (mode == DeformMode.FFD) { ApplyFfd(); return; }

            Vector3[] src = model.RestVertices;
            Vector3 min = model.ModelBounds.min;
            Vector3 max = model.ModelBounds.max;
            var dst = new Vector3[src.Length];

            switch (mode)
            {
                case DeformMode.Taper:
                    for (int i = 0; i < src.Length; i++)
                        dst[i] = DeformMath.Taper(src[i], min, max, axis, taperAmount);
                    break;
                case DeformMode.Twist:
                    for (int i = 0; i < src.Length; i++)
                        dst[i] = DeformMath.Twist(src[i], min, max, axis, twistDeg * Mathf.Deg2Rad);
                    break;
                case DeformMode.Bend:
                    for (int i = 0; i < src.Length; i++)
                        dst[i] = DeformMath.Bend(src[i], min, max, axis, bendDeg, bendCenter, bendDir);
                    break;
                default: // None
                    dst = (Vector3[])src.Clone();
                    break;
            }

            model.SetVertices(dst);
            model.RecalculateNormalsAndBounds();
        }

        public void ResetDeformation()
        {
            mode = DeformMode.None;
            ClearFfdVisual();
            ffd = null;
            embeddedParams = null;
            if (model != null && model.IsLoaded) model.RestoreRestPose();
            if (ui != null) ui.RefreshMode();
        }

        // =====================================================================
        //  FFD
        // =====================================================================
        /// <summary>① 初始状态：算包围盒 → n×n×n 网格点 → 顶点嵌入参数坐标。</summary>
        public void BuildFfd()
        {
            if (model == null || !model.IsLoaded) return;
            ClearFfdVisual();

            ffd = new FfdLattice();
            ffd.Build(model.ModelBounds, ffdResolution);

            // 嵌入：每个顶点 → (s,t,u)（此后固定）
            var src = model.RestVertices;
            embeddedParams = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) embeddedParams[i] = ffd.Embed(src[i]);

            // 可视化：n³ 个控制点球 + 格子线框
            ffdRoot = new GameObject("FFD Lattice").transform;
            ffdRoot.SetParent(transform, false);
            float maxDim = Mathf.Max(model.ModelBounds.size.x, model.ModelBounds.size.y, model.ModelBounds.size.z);
            float handleSize = Mathf.Max(0.08f, maxDim * 0.028f);

            ffdHandles = new List<GameObject>(ffd.ControlPointCount);
            for (int idx = 0; idx < ffd.ControlPointCount; idx++)
            {
                FfdLattice.FromIndex(idx, ffd.n, out int i, out int j, out int k);
                ffdHandles.Add(DeformationFactory.CreateHandle(ffd.current[i, j, k], handleSize, ffdRoot, "cp_" + idx));
            }
            var wireGo = DeformationFactory.CreateLatticeWireObject(ffd, ffdRoot);
            ffdWireFilter = wireGo.GetComponent<MeshFilter>();
            ffdWireMesh = ffdWireFilter.sharedMesh;

            ffdActive = true;
            ApplyFfd();   // 初始控制点不动 → 顶点应保持不变（自检会验证这一点）
        }

        /// <summary>④ 终止状态：拖动控制点后，按 Bernstein 张量积重算全部顶点。</summary>
        public void MoveFfdHandle(int idx, Vector3 newPos)
        {
            if (ffd == null) return;
            FfdLattice.FromIndex(idx, ffd.n, out int i, out int j, out int k);
            ffd.current[i, j, k] = newPos;
            if (ffdHandles != null && idx >= 0 && idx < ffdHandles.Count && ffdHandles[idx] != null)
                ffdHandles[idx].transform.position = newPos;
            DeformationFactory.UpdateLatticeWireframe(ffdWireMesh, ffd);
            ApplyFfd();
        }

        public void ApplyFfd()
        {
            if (ffd == null || embeddedParams == null || model == null || !model.IsLoaded) return;
            var src = model.RestVertices;
            var dst = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = ffd.Deform(embeddedParams[i]);
            model.SetVertices(dst);
            model.RecalculateNormalsAndBounds();
        }

        public void ResetFfdLattice()
        {
            if (ffd == null) return;
            ffd.Reset();
            for (int idx = 0; idx < ffd.ControlPointCount; idx++)
            {
                FfdLattice.FromIndex(idx, ffd.n, out int i, out int j, out int k);
                if (ffdHandles != null && idx < ffdHandles.Count && ffdHandles[idx] != null)
                {
                    ffdHandles[idx].transform.position = ffd.current[i, j, k];
                    DeformationFactory.SetHandleSelected(ffdHandles[idx], false);
                }
            }
            DeformationFactory.UpdateLatticeWireframe(ffdWireMesh, ffd);
            ApplyFfd();
            if (interaction != null) interaction.ClearSelection();
        }

        void ClearFfdVisual()
        {
            if (ffdRoot != null) Destroy(ffdRoot.gameObject);
            ffdRoot = null;
            ffdHandles = null;
            ffdWireMesh = null;
            ffdWireFilter = null;
            ffdActive = false;
        }

        // =====================================================================
        //  加载模型（换模型入口）
        // =====================================================================
        public void LoadProcedural(string type) => model?.LoadProcedural(type);

        public void LoadObjFile(string absolutePath) => model?.LoadFromFile(absolutePath);

        public void LoadStreamingObj(string fileName)
        {
            if (model == null) return;
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "models", fileName);
            model.LoadFromFile(path);
        }
    }
}
