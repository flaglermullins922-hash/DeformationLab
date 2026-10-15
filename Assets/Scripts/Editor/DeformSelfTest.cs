// =============================================================================
// DeformSelfTest.cs —— 数学自检：验证 Taper / Twist / Bend / FFD 公式正确性
//
// 全部是纯数学断言，不依赖场景 / 渲染，可在批处理下运行并输出报告。
// 覆盖：
//   Taper：k=0 恒等 / 主轴不变 / 垂直轴缩放 / 中心线不动
//   Twist：angle=0 恒等 / 顶部转满角、底部不转 / 与 Unity Quaternion 对拍
//   Bend ：θ=0 恒等 / 中心线顶点落圆弧 / 弧角正确
//   FFD  ：初始格子不动=恒等 / Bernstein 单位分解 / 线性精度 / 移动控制点跟随
// =============================================================================

using System.Text;
using UnityEngine;

namespace DeformationLab.EditorTools
{
    public static class DeformSelfTest
    {
        static int _pass, _fail;
        static StringBuilder _sb;
        const string ReportPath = "D:/un/DeformationLab/docs/数学自检报告.txt";

        [UnityEditor.MenuItem("全局变形作业/4. 数学自检")]
        public static void Run() => RunInternal();

        public static void RunFromCLI() => RunInternal();

        static void RunInternal()
        {
            _pass = 0; _fail = 0;
            _sb = new StringBuilder();
            _sb.AppendLine("DeformationLab 数学自检报告");
            _sb.AppendLine("================================");
            _sb.AppendLine("生成时间: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            TestTaper();
            TestTwist();
            TestBend();
            TestFfd();

            _sb.AppendLine();
            _sb.AppendLine("================================");
            _sb.AppendLine("结果: " + _pass + " 通过 / " + _fail + " 失败" + (_fail == 0 ? "  ✔ 全部通过" : "  ✘ 存在失败"));

            Debug.Log(_sb.ToString());
            System.IO.Directory.CreateDirectory("D:/un/DeformationLab/docs");
            System.IO.File.WriteAllText(ReportPath, _sb.ToString(), Encoding.UTF8);
            Debug.Log("[DeformSelfTest] 报告已写入 " + ReportPath);
        }

        static void Check(string name, bool ok, string detail = "")
        {
            _sb.AppendLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "  —— " + detail));
            if (ok) _pass++; else _fail++;
        }

        static bool Approx(float a, float b, float eps = 1e-4f) => Mathf.Abs(a - b) < eps;
        static bool Approx(Vector3 a, Vector3 b, float eps = 1e-3f) => (a - b).sqrMagnitude < eps * eps;

        // =========================================================================
        static void TestTaper()
        {
            _sb.AppendLine("\n【一】Taper 锥化");
            Vector3 min = new Vector3(0, 0, 0), max = new Vector3(2, 4, 2);
            Vector3 center = (min + max) * 0.5f;   // (1,2,1)

            // 1. k=0 恒等
            Vector3 p = new Vector3(0.5f, 1f, 0.5f);
            Check("k=0 顶点不变", Approx(DeformMath.Taper(p, min, max, DeformAxis.Y, 0f), p));

            // 2. k≠0：主轴坐标不变，垂直轴相对中心线缩放
            float k = 0.5f;
            Vector3 r2 = DeformMath.Taper(p, min, max, DeformAxis.Y, k);
            float s = 1f + k * 0.25f;             // t=(1-0)/4=0.25, s=1.125
            Vector3 expect = new Vector3(center.x + (p.x - center.x) * s, p.y, center.z + (p.z - center.z) * s);
            Check("k=0.5 主轴不变+垂直轴缩放正确", Approx(r2, expect), "r=" + r2 + " expect=" + expect);

            // 3. 中心线上的顶点不因锥化移动（偏移为 0，缩放无效果）
            Vector3 onAxis = new Vector3(1f, 3f, 1f);
            Check("中心线顶点不移动", Approx(DeformMath.Taper(onAxis, min, max, DeformAxis.Y, k), onAxis));

            // 4. 顶部偏离顶点被放大：t=1 → s=1.5
            Vector3 off = new Vector3(1.5f, 4f, 1f);
            Vector3 rOff = DeformMath.Taper(off, min, max, DeformAxis.Y, k);
            Check("顶部偏离顶点 x 被放大到 1.75", Approx(rOff.x, 1.75f), "x=" + rOff.x);
        }

        static void TestTwist()
        {
            _sb.AppendLine("\n【二】Twist 扭曲");
            Vector3 min = new Vector3(-1, -2, -1), max = new Vector3(1, 2, 1);   // center (0,0,0)

            Vector3 p = new Vector3(1f, 1f, 0f);
            Check("angle=0 顶点不变", Approx(DeformMath.Twist(p, min, max, DeformAxis.Y, 0f), p));

            float angle = Mathf.PI / 2f;   // 90°
            Vector3 top = new Vector3(1f, 2f, 0f);       // t=1
            Vector3 rTop = DeformMath.Twist(top, min, max, DeformAxis.Y, angle);
            Check("顶部绕Y转90°=(0,2,-1)", Approx(rTop, new Vector3(0f, 2f, -1f)), "rTop=" + rTop);

            Vector3 bottom = new Vector3(1f, -2f, 0f);   // t=0
            Check("底部不转", Approx(DeformMath.Twist(bottom, min, max, DeformAxis.Y, angle), bottom));

            // 与 Unity Quaternion 对拍
            Vector3 q = new Vector3(0.7f, 1.2f, -0.5f);
            float a2 = 1.234f;
            Vector3 rUnity = Quaternion.AngleAxis(a2 * Mathf.Rad2Deg, Vector3.up) * q;
            Vector3 rMine = DeformMath.RotateAroundAxis(q, DeformAxis.Y, a2);
            Check("绕Y旋转与Unity Quaternion一致", Approx(rMine, rUnity, 1e-3f), "mine=" + rMine + " unity=" + rUnity);
        }

        static void TestBend()
        {
            _sb.AppendLine("\n【三】Bend 弯曲");
            Vector3 min = new Vector3(-1, 0, -1), max = new Vector3(1, 4, 1);   // H=4, 中心线 x=0,z=0

            Vector3 p = new Vector3(0.5f, 3f, 0.5f);
            Check("θ=0 顶点不变", Approx(DeformMath.Bend(p, min, max, DeformAxis.Y, 0f, 0.5f, DeformAxis.X), p));

            // 中心线顶点落圆弧（θ=90°）
            float theta = 90f;
            float H = 4f, yc = 2f;                     // center01=0.5
            Vector3 onAxis = new Vector3(0f, 4f, 0f);  // 中心线顶部
            Vector3 r = DeformMath.Bend(onAxis, min, max, DeformAxis.Y, theta, 0.5f, DeformAxis.X);

            float R = H / (theta * Mathf.Deg2Rad);     // 8/π
            float s = 4f - yc;                          // 2
            float phi = s / R;                          // π/4
            Vector3 expect = new Vector3(R * (1f - Mathf.Cos(phi)), yc + R * Mathf.Sin(phi), 0f);
            Check("中心线顶点落圆弧", Approx(r, expect, 1e-2f), "r=" + r + " expect=" + expect);
            Check("顶部弧角 φ = π/4", Approx(phi, Mathf.PI / 4f, 1e-3f), "φ=" + phi);

            // 弯曲后顶点整体仍在同一竖直平面内（z 不变）
            Vector3 offAxis = new Vector3(0.3f, 1.5f, 0.4f);
            Vector3 rOff = DeformMath.Bend(offAxis, min, max, DeformAxis.Y, theta, 0.5f, DeformAxis.X);
            Check("弯曲不改变 z 坐标", Approx(rOff.z, offAxis.z));
        }

        static void TestFfd()
        {
            _sb.AppendLine("\n【四】FFD 自由变形");
            var bounds = new Bounds(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));  // [0,1]³

            // 1. 初始格子（不动控制点）重建 = 恒等
            var ffd = new FfdLattice();
            ffd.Build(bounds, 4);
            Vector3[] pts = {
                new Vector3(0.1f, 0.2f, 0.3f), new Vector3(0.9f, 0.8f, 0.7f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f)
            };
            bool allId = true;
            foreach (var v in pts)
                if (!Approx(ffd.Deform(ffd.Embed(v)), v, 1e-3f)) { allId = false; break; }
            Check("初始格子重建=恒等（嵌入+变形无损）", allId);

            // 2. Bernstein 单位分解 Σ B_i(u) = 1
            bool unity = true;
            for (int n = 2; n <= 4 && unity; n++)
            {
                var g = new FfdLattice();
                g.Build(bounds, n);
                for (int t = 0; t <= 10; t++)
                {
                    float u = t / 10f, sum = 0f;
                    for (int i = 0; i < n; i++) sum += g.Bernstein(i, u);
                    if (!Approx(sum, 1f, 1e-4f)) { unity = false; break; }
                }
            }
            Check("Bernstein 单位分解 ΣB=1（n=2/3/4）", unity);

            // 3. Bernstein 线性精度 Σ B_i(u)·i/(n-1) = u
            bool linPrec = true;
            var g3 = new FfdLattice();
            g3.Build(bounds, 4);
            for (int t = 0; t <= 8 && linPrec; t++)
            {
                float u = t / 8f, sum = 0f;
                for (int i = 0; i < 4; i++) sum += g3.Bernstein(i, u) * (i / 3f);
                if (!Approx(sum, u, 1e-4f)) linPrec = false;
            }
            Check("Bernstein 线性精度 ΣB·i/(n-1)=u", linPrec);

            // 4. 移动角点控制点 → 顶点按 Bernstein 权重跟随
            var f2 = new FfdLattice();
            f2.Build(bounds, 4);
            f2.current[3, 3, 3] = new Vector3(2f, 2f, 2f);   // 把 (1,1,1) 角点拖到 (2,2,2)
            Vector3 vv = new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 rv = f2.Deform(f2.Embed(vv));
            float b3 = 0.5f * 0.5f * 0.5f;                  // B_3(0.5) = 0.125
            float w = b3 * b3 * b3;                         // 0.125³
            Vector3 exp2 = new Vector3(0.5f + w, 0.5f + w, 0.5f + w);
            Check("移动角点→顶点按Bernstein跟随", Approx(rv, exp2, 1e-4f), "rv=" + rv + " expect=" + exp2);

            // 5. 复位后恢复恒等
            f2.Reset();
            Check("Reset 后恢复恒等", Approx(f2.Deform(f2.Embed(vv)), vv, 1e-3f));
        }
    }
}
