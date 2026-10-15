// =============================================================================
// DeformMath.cs —— 全局变形数学核心：Taper 锥化 / Twist 扭曲 / Bend 弯曲
// （FFD 自由变形见 FfdLattice.cs）
//
// 这三种"解析式全局变形"都由 Barr(1984) 提出，本质是给每个顶点一个
// 随"主轴位置 t∈[0,1]"变化的变换：
//   Taper：在垂直于主轴的平面内，缩放系数 s(t) = 1 + k·t 随 t 线性变化
//   Twist：绕主轴旋转，角度 θ(t) = angle·t 随 t 线性变化
//   Bend ：把"直线主轴"映射到"圆弧"，弯曲角沿主轴线性累计
//
// 三个函数都是【纯函数】：输入原始顶点 + 模型包围盒 + 变形参数，输出新顶点。
// 不依赖场景、不依赖 Unity 组件，因此可以直接被数学自检脚本单元测试。
// =============================================================================

using UnityEngine;

namespace DeformationLab
{
    /// <summary>变形主轴（长度方向）。所有变形都围绕这条轴定义。</summary>
    public enum DeformAxis { X = 0, Y = 1, Z = 2 }

    public static class DeformMath
    {
        // ---------------------------------------------------------------------
        // 主轴归一化参数 t ∈ [0,1]：顶点在主轴方向上、相对包围盒 [min,max] 的位置
        // ---------------------------------------------------------------------
        public static float AxisParam(Vector3 p, Vector3 min, Vector3 max, DeformAxis axis)
        {
            float span = max[(int)axis] - min[(int)axis];
            if (Mathf.Abs(span) < 1e-6f) return 0f;
            return (p[(int)axis] - min[(int)axis]) / span;
        }

        // ---------------------------------------------------------------------
        // Taper 锥化：s(t) = 1 + k·t，在垂直主轴的平面内相对"中心线"缩放
        //   k > 0 → 顶部放大（上粗下细）；k < 0 → 顶部收窄（上尖下宽）
        //   k = 0 → 不变
        // ---------------------------------------------------------------------
        public static Vector3 Taper(Vector3 p, Vector3 min, Vector3 max, DeformAxis axis, float k)
        {
            float t = AxisParam(p, min, max, axis);
            float s = 1f + k * t;
            Vector3 center = (min + max) * 0.5f;
            Vector3 r = p;
            for (int c = 0; c < 3; c++)
                if (c != (int)axis)
                    r[c] = center[c] + (p[c] - center[c]) * s;
            return r;
        }

        // ---------------------------------------------------------------------
        // Twist 扭曲：θ(t) = angle·t，绕主轴旋转，角度随高度线性累计
        //   angle 是主轴"顶端相对底端"的总扭转角（弧度）
        // ---------------------------------------------------------------------
        public static Vector3 Twist(Vector3 p, Vector3 min, Vector3 max, DeformAxis axis, float angleRad)
        {
            float t = AxisParam(p, min, max, axis);
            float theta = angleRad * t;
            Vector3 center = (min + max) * 0.5f;
            return center + RotateAroundAxis(p - center, axis, theta);
        }

        /// <summary>绕坐标轴旋转（右手系，与 Unity Quaternion.AngleAxis 一致）。</summary>
        public static Vector3 RotateAroundAxis(Vector3 v, DeformAxis axis, float theta)
        {
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            switch (axis)
            {
                case DeformAxis.X: return new Vector3(v.x, v.y * c - v.z * s, v.y * s + v.z * c);
                case DeformAxis.Y: return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
                default:           return new Vector3(v.x * c - v.y * s, v.x * s + v.y * c, v.z);
            }
        }

        // ---------------------------------------------------------------------
        // Bend 弯曲：主轴 axis 是"长度方向"，bendDir 是弯曲方向，绕第三条轴弯成圆弧。
        //   设 R = H/θ 为弯曲半径（H=主轴高度，θ=总弯曲角），
        //   s = 顶点相对弯曲中心的弧长，φ = s/R，off = 顶点在 bendDir 方向的偏移：
        //     new(bendDir) = R·(1 - cosφ) + off·cosφ
        //     new(axis)    = yc + R·sinφ - off·sinφ
        //   θ→0 时 R→∞，公式退化为恒等变换（代码里对小角度直接短路）。
        //   center01 ∈ [0,1] 指定弯曲中心沿主轴的位置（0.5 = 中间）。
        // ---------------------------------------------------------------------
        public static Vector3 Bend(Vector3 p, Vector3 min, Vector3 max, DeformAxis axis,
                                   float bendDeg, float center01, DeformAxis bendDir)
        {
            float H = max[(int)axis] - min[(int)axis];
            if (Mathf.Abs(H) < 1e-6f) return p;
            float theta = bendDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(theta) < 1e-4f) return p;   // 几乎不弯，退化为恒等

            float lo = min[(int)axis];
            float yc = lo + center01 * H;             // 弯曲中心的轴坐标
            float s = p[(int)axis] - yc;              // 相对弯曲中心的弧长
            float cDir = (min[(int)bendDir] + max[(int)bendDir]) * 0.5f;
            float off = p[(int)bendDir] - cDir;       // 弯曲方向上的偏移（相对中心线）

            float R = H / theta;                      // 弯曲半径（θ 带符号 → R 带符号 → 方向可控）
            float phi = s / R;
            float cosPhi = Mathf.Cos(phi), sinPhi = Mathf.Sin(phi);

            float newOff = R * (1f - cosPhi) + off * cosPhi;   // 沿 bendDir 的新偏移
            float newLen = yc + R * sinPhi - off * sinPhi;     // 沿 axis 的新位置

            Vector3 r = p;
            r[(int)axis] = newLen;
            r[(int)bendDir] = cDir + newOff;
            return r;
        }
    }
}
