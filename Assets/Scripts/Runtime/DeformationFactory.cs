// =============================================================================
// DeformationFactory.cs —— 物体工厂：场景环境 / FFD 控制点 / 格子线框
//
// 场景内容全部在运行时由代码生成（零 Inspector 拖拽），与 QuaternionLab /
// BezierCurveLab 的工程风格一致：场景文件里几乎为空，程序启动后自动搭建。
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace DeformationLab
{
    public static class DeformationFactory
    {
        static Mesh _handleMesh;                 // 共享的控制点小球 mesh
        static Material _handleMat;              // 普通控制点材质
        static Material _handleSelectedMat;      // 选中高亮材质

        static Shader GetUnlit()
        {
            Shader s = Shader.Find("DeformationLab/Unlit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        // ---------------------------------------------------------------------
        // 场景基础环境：相机 + 两盏方向光 + 地面。返回主相机。
        // ---------------------------------------------------------------------
        public static Camera BuildEnvironment()
        {
            // 主光（暖白，斜上方）
            var keyGo = new GameObject("Directional Light");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.05f;
            key.color = new Color(1f, 0.97f, 0.92f);
            keyGo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);

            // 补光（冷色，从另一侧）
            var fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.65f, 0.75f, 0.95f);
            fillGo.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

            // 地面（帮助感知空间与投影）
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -4.2f, 0f);
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            var gmat = new Material(GetUnlit()) { color = new Color(0.14f, 0.15f, 0.17f) };
            ground.GetComponent<Renderer>().sharedMaterial = gmat;
            var gc = ground.GetComponent<Collider>();
            if (gc != null) Object.Destroy(gc);   // 地面不需要碰撞体（用屏幕拾取）

            // 主相机
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.transform.position = new Vector3(7f, 5f, 8f);
            cam.transform.LookAt(Vector3.zero);
            return cam;
        }

        // ---------------------------------------------------------------------
        // 共享的控制点小球（懒加载单例）
        // ---------------------------------------------------------------------
        public static Mesh HandleMesh
        {
            get
            {
                if (_handleMesh == null) _handleMesh = ProceduralMeshBuilder.BuildSphere(1f, 10, 6);
                return _handleMesh;
            }
        }

        public static Material HandleMaterial
        {
            get
            {
                if (_handleMat == null)
                {
                    _handleMat = new Material(GetUnlit());
                    _handleMat.color = new Color(0.40f, 0.85f, 0.95f);   // 青色
                    _handleMat.SetColor("_Emission", new Color(0.05f, 0.12f, 0.14f));
                }
                return _handleMat;
            }
        }

        public static Material HandleSelectedMaterial
        {
            get
            {
                if (_handleSelectedMat == null)
                {
                    _handleSelectedMat = new Material(GetUnlit());
                    _handleSelectedMat.color = new Color(1f, 0.55f, 0.15f);  // 橙色高亮
                    _handleSelectedMat.SetColor("_Emission", new Color(0.4f, 0.2f, 0f));
                }
                return _handleSelectedMat;
            }
        }

        /// <summary>创建一个 FFD 控制点小球（共享 mesh + 材质）。</summary>
        public static GameObject CreateHandle(Vector3 pos, float size, Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = HandleMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = HandleMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        public static void SetHandleSelected(GameObject handle, bool selected)
        {
            if (handle == null) return;
            var mr = handle.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sharedMaterial = selected ? HandleSelectedMaterial : HandleMaterial;
            // 选中时略微放大，视觉更醒目
            handle.transform.localScale = Vector3.one * (selected ? 1.5f : 1f);
        }

        // ---------------------------------------------------------------------
        // FFD 格子线框：把相邻控制点连成线段（MeshTopology.Lines）
        // ---------------------------------------------------------------------
        public static Mesh BuildLatticeWireframe(FfdLattice ffd)
        {
            int n = ffd.n;
            var lines = new List<int>();

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                    {
                        int a = FfdLattice.IndexOf(i, j, k, n);
                        if (i + 1 < n) { lines.Add(a); lines.Add(FfdLattice.IndexOf(i + 1, j, k, n)); }
                        if (j + 1 < n) { lines.Add(a); lines.Add(FfdLattice.IndexOf(i, j + 1, k, n)); }
                        if (k + 1 < n) { lines.Add(a); lines.Add(FfdLattice.IndexOf(i, j, k + 1, n)); }
                    }

            var mesh = new Mesh { name = "FfdLatticeWire" };
            mesh.vertices = FlattenControlPoints(ffd.current, n);
            mesh.SetIndices(lines.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3[] FlattenControlPoints(Vector3[,,] pts, int n)
        {
            var arr = new Vector3[n * n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                        arr[FfdLattice.IndexOf(i, j, k, n)] = pts[i, j, k];
            return arr;
        }

        /// <summary>更新线框 mesh 的顶点（控制点拖动后调用）。</summary>
        public static void UpdateLatticeWireframe(Mesh mesh, FfdLattice ffd)
        {
            if (mesh == null || ffd == null) return;
            mesh.vertices = FlattenControlPoints(ffd.current, ffd.n);
            mesh.RecalculateBounds();
        }

        /// <summary>创建格子线框显示对象。</summary>
        public static GameObject CreateLatticeWireObject(FfdLattice ffd, Transform parent)
        {
            var go = new GameObject("FfdLatticeWire");
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildLatticeWireframe(ffd);
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(GetUnlit());
            mat.color = new Color(0.30f, 0.75f, 0.85f);
            mat.SetColor("_Emission", new Color(0.10f, 0.24f, 0.28f));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
