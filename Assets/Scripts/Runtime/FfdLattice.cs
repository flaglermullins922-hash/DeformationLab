// =============================================================================
// FfdLattice.cs —— FFD（Free-Form Deformation，自由变形）格子
//
// 实现 Sederberg & Parry (1986) 的经典三变量 FFD：
//   ① 初始状态：计算模型包围盒 → 在包围盒上均匀摆放 n×n×n 个控制点
//   ② 嵌入：把每个模型顶点换算成格子里的参数坐标 (s,t,u) ∈ [0,1]³（此后固定）
//   ③ 变形：用户拖动控制点 → 顶点新位置 = 控制点的 Bernstein 张量积
//        v' = Σᵢ Σⱼ Σₖ Bᵢ(s)·Bⱼ(t)·Bₖ(u) · P[i,j,k]
//   ④ 终止状态：控制点被改 → 模型随之改变（顶点"粘"在参数空间里）
//
// 关键点：顶点的参数坐标 (s,t,u) 在嵌入后【永不改变】。拖动的是控制点，
// 顶点通过 Bernstein 权重"跟随"控制点移动 —— 这就是 FFD 与"直接拖顶点"的本质区别。
//
// n 取值：2 = 线性（三线性插值）· 3 = 二次 · 4 = 三次（最经典，默认）。
// 本类是纯数据类，不含任何 GameObject/Transform，可被数学自检直接调用。
// =============================================================================

using UnityEngine;

namespace DeformationLab
{
    public class FfdLattice
    {
        /// <summary>每边控制点数（2/3/4）。</summary>
        public int n { get; private set; } = 4;

        /// <summary>包围盒最小角（格子局部坐标系原点）。</summary>
        public Vector3 origin;

        /// <summary>包围盒尺寸。</summary>
        public Vector3 size;

        /// <summary>初始控制点（均匀格子，变形前）。</summary>
        public Vector3[,,] initial;

        /// <summary>当前控制点（可被拖动，变形后）。</summary>
        public Vector3[,,] current;

        /// <summary>Bernstein 多项式次数 = n - 1。</summary>
        public int Degree => n - 1;

        /// <summary>控制点总数 = n³。</summary>
        public int ControlPointCount => n * n * n;

        // ---------------------------------------------------------------------
        // ① 初始状态：按包围盒建 n×n×n 的均匀控制点格子
        // ---------------------------------------------------------------------
        public void Build(Bounds bounds, int resolution)
        {
            n = Mathf.Clamp(resolution, 2, 4);
            origin = bounds.min;
            size = bounds.size;
            initial = new Vector3[n, n, n];
            current = new Vector3[n, n, n];

            float div = Mathf.Max(1, n - 1);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 p = new Vector3(
                            origin.x + size.x * i / div,
                            origin.y + size.y * j / div,
                            origin.z + size.z * k / div);
                        initial[i, j, k] = p;
                        current[i, j, k] = p;
                    }
        }

        // ---------------------------------------------------------------------
        // ② 嵌入：顶点 → 参数坐标 (s,t,u)。初始格子均匀，故等价于线性归一化。
        // ---------------------------------------------------------------------
        public Vector3 Embed(Vector3 v)
        {
            return new Vector3(
                (v.x - origin.x) / (size.x + 1e-6f),
                (v.y - origin.y) / (size.y + 1e-6f),
                (v.z - origin.z) / (size.z + 1e-6f));
        }

        // ---------------------------------------------------------------------
        // ③④ 变形：按当前控制点 + 参数坐标，用 Bernstein 张量积重算顶点
        // ---------------------------------------------------------------------
        public Vector3 Deform(Vector3 param)
        {
            float s = Mathf.Clamp01(param.x);
            float t = Mathf.Clamp01(param.y);
            float u = Mathf.Clamp01(param.z);

            // 预计算三个方向的 Bernstein 基函数（3n 次，而非 n³ 次）
            float[] bs = new float[n], bt = new float[n], bu = new float[n];
            for (int i = 0; i < n; i++) { bs[i] = Bernstein(i, s); bt[i] = Bernstein(i, t); bu[i] = Bernstein(i, u); }

            Vector3 result = Vector3.zero;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                        result += current[i, j, k] * (bs[i] * bt[j] * bu[k]);
            return result;
        }

        /// <summary>Bernstein 基函数 B_{i,m}(u) = C(m,i)·u^i·(1-u)^(m-i)，m = n-1。</summary>
        public float Bernstein(int i, float u)
        {
            int m = n - 1;
            return Binomial(m, i) * Mathf.Pow(u, i) * Mathf.Pow(1f - u, m - i);
        }

        /// <summary>二项式系数 C(m,i)（m≤4，数值很小，直接算）。</summary>
        public static int Binomial(int m, int i)
        {
            if (i < 0 || i > m) return 0;
            long r = 1;
            for (int k = 1; k <= i; k++) r = r * (m - i + k) / k;
            return (int)r;
        }

        /// <summary>把当前控制点恢复成初始均匀格子。</summary>
        public void Reset()
        {
            if (initial == null || current == null) return;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                        current[i, j, k] = initial[i, j, k];
        }

        /// <summary>把三维索引 (i,j,k) 拍平成 [0, n³) 的一维编号（供 UI/可视化使用）。</summary>
        public static int IndexOf(int i, int j, int k, int n) => (i * n + j) * n + k;

        /// <summary>把一维编号还原成三维索引 (i,j,k)。</summary>
        public static void FromIndex(int idx, int n, out int i, out int j, out int k)
        {
            k = idx % n; idx /= n;
            j = idx % n; idx /= n;
            i = idx;
        }
    }
}
