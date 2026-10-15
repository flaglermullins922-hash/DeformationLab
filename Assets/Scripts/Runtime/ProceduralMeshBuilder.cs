// =============================================================================
// ProceduralMeshBuilder.cs —— 程序化生成"内置演示模型"的 Mesh
//
// 作业要求"能够导入 obj 文件"（走 ObjParsing.cs），但为了开箱就能演示变形，
// 工程内置了三个程序化模型作为默认演示对象。它们都带：
//   · 法线 / UV（渲染正常）
//   · 按高度渐变的顶点色（青→紫→橙），变形后能一眼看出"原来哪一段被移到了哪里"
//
// 其中"细长圆柱"是主打：径向 24 段 + 高度 48 段，弯曲/扭曲/锥化效果最平滑直观。
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace DeformationLab
{
    public static class ProceduralMeshBuilder
    {
        /// <summary>青 → 紫 → 橙 的高度渐变顶点色。</summary>
        public static Color Gradient(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.5f) return Color.Lerp(new Color(0.15f, 0.85f, 1f), new Color(0.55f, 0.45f, 1f), t * 2f);
            return Color.Lerp(new Color(0.55f, 0.45f, 1f), new Color(1f, 0.55f, 0.20f), (t - 0.5f) * 2f);
        }

        // ---------------------------------------------------------------------
        // 细长圆柱（侧壁 + 顶盖 + 底盖），Y 轴为高度方向
        // ---------------------------------------------------------------------
        public static Mesh BuildCylinder(float radius, float height, int radialSeg, int heightSeg)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();

            // ---- 侧壁 ----
            for (int j = 0; j <= heightSeg; j++)
            {
                float t = (float)j / heightSeg;
                float y = -height * 0.5f + height * t;
                for (int i = 0; i <= radialSeg; i++)
                {
                    float a = (float)i / radialSeg * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    verts.Add(new Vector3(c * radius, y, s * radius));
                    normals.Add(new Vector3(c, 0f, s));
                    uvs.Add(new Vector2((float)i / radialSeg, t));
                    colors.Add(Gradient(t));
                }
            }
            int row = radialSeg + 1;
            for (int j = 0; j < heightSeg; j++)
                for (int i = 0; i < radialSeg; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            AddCap(verts, normals, uvs, colors, tris, radius, height * 0.5f, +1, radialSeg, 1f);
            AddCap(verts, normals, uvs, colors, tris, radius, -height * 0.5f, -1, radialSeg, 0f);

            return Assemble("Cylinder", verts, normals, uvs, colors, tris);
        }

        static void AddCap(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
                           List<Color> colors, List<int> tris, float radius, float y,
                           int sign, int radialSeg, float gradT)
        {
            int center = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            normals.Add(new Vector3(0f, sign, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            colors.Add(Gradient(gradT));

            for (int i = 0; i <= radialSeg; i++)
            {
                float a = (float)i / radialSeg * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(c * radius, y, s * radius));
                normals.Add(new Vector3(0f, sign, 0f));
                uvs.Add(new Vector2(c * 0.5f + 0.5f, s * 0.5f + 0.5f));
                colors.Add(Gradient(gradT));
            }
            for (int i = 0; i < radialSeg; i++)
            {
                if (sign > 0) { tris.Add(center); tris.Add(center + 1 + i + 1); tris.Add(center + 1 + i); }
                else          { tris.Add(center); tris.Add(center + 1 + i); tris.Add(center + 1 + i + 1); }
            }
        }

        // ---------------------------------------------------------------------
        // 立方体（Y 方向细分，便于弯曲/扭曲；XZ 各 1 段保持棱角）
        // ---------------------------------------------------------------------
        public static Mesh BuildBox(float sx, float sy, float sz, int segY)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();

            float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
            AddBoxSide(verts, normals, uvs, colors, tris, hx, hy, hz, segY);
            return Assemble("Box", verts, normals, uvs, colors, tris);
        }

        static void AddBoxSide(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
                               List<Color> colors, List<int> tris, float hx, float hy, float hz, int segY)
        {
            // 4 个侧面，每个面是 (segY+1) 层 × 2 列；再加顶、底面
            var sides = new (Vector3 n, Vector3 a, Vector3 b)[]
            {
                (Vector3.right,   new Vector3( hx,-hy, hz), new Vector3( hx, hy, hz)), // +X  (b-a 沿 +Y)
                (Vector3.left,    new Vector3(-hx,-hy,-hz), new Vector3(-hx, hy,-hz)), // -X
                (Vector3.forward, new Vector3(-hx,-hy, hz), new Vector3(-hx, hy, hz)), // +Z
                (Vector3.back,    new Vector3( hx,-hy,-hz), new Vector3( hx, hy,-hz)), // -Z
            };
            foreach (var s in sides)
            {
                int start = verts.Count;
                Vector3 edge = s.b - s.a;              // 沿 Y 的边
                Vector3 across = Vector3.Cross(s.n, Vector3.up).normalized; // 面的横向
                // 横向长度 = 面的宽（±X 面宽 = 2hz，±Z 面宽 = 2hx）
                float width = s.n.x != 0 ? 2f * hz : 2f * hx;
                for (int v = 0; v <= segY; v++)
                {
                    float tv = (float)v / segY;
                    for (int u = 0; u <= 1; u++)
                    {
                        Vector3 p = s.a + Vector3.up * (tv * 2f * hy) + across * (u * width);
                        verts.Add(p);
                        normals.Add(s.n);
                        uvs.Add(new Vector2((float)u, tv));
                        colors.Add(Gradient(tv));
                    }
                }
                for (int v = 0; v < segY; v++)
                    for (int u = 0; u < 1; u++)
                    {
                        int a = start + v * 2 + u, b = a + 1, c = a + 2, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
            }
            // 顶面、底面（各 4 顶点 quad）
            AddFlatQuad(verts, normals, uvs, colors, tris, new Vector3(0, hy, 0), Vector3.up,
                        new Vector3(-hx, hy, -hz), new Vector3( hx, hy,  hz), 1f);
            AddFlatQuad(verts, normals, uvs, colors, tris, new Vector3(0, -hy, 0), Vector3.down,
                        new Vector3(-hx, -hy,  hz), new Vector3( hx, -hy, -hz), 0f);
        }

        static void AddFlatQuad(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs,
                                List<Color> colors, List<int> tris, Vector3 n, Vector3 dir,
                                Vector3 c0, Vector3 c1, float gradT)
        {
            int start = verts.Count;
            verts.Add(c0); verts.Add(new Vector3(c1.x, c0.y, c0.z));
            verts.Add(c1); verts.Add(new Vector3(c0.x, c0.y, c1.z));
            for (int i = 0; i < 4; i++) { normals.Add(dir); uvs.Add(Vector2.zero); colors.Add(Gradient(gradT)); }
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
        }

        // ---------------------------------------------------------------------
        // 经纬球
        // ---------------------------------------------------------------------
        public static Mesh BuildSphere(float radius, int lonSeg, int latSeg)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();

            for (int la = 0; la <= latSeg; la++)
            {
                float theta = (float)la / latSeg * Mathf.PI;         // 0=顶, π=底
                float y = Mathf.Cos(theta) * radius;
                float ring = Mathf.Sin(theta) * radius;
                float gradT = (float)la / latSeg;                     // 顶(0)~底(1)
                for (int lo = 0; lo <= lonSeg; lo++)
                {
                    float phi = (float)lo / lonSeg * Mathf.PI * 2f;
                    Vector3 n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    verts.Add(n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2((float)lo / lonSeg, (float)la / latSeg));
                    colors.Add(Gradient(1f - gradT));                 // 顶蓝底橙
                }
            }
            int row = lonSeg + 1;
            for (int la = 0; la < latSeg; la++)
                for (int lo = 0; lo < lonSeg; lo++)
                {
                    int a = la * row + lo, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            return Assemble("Sphere", verts, normals, uvs, colors, tris);
        }

        // ---------------------------------------------------------------------
        static Mesh Assemble(string name, List<Vector3> verts, List<Vector3> normals,
                             List<Vector2> uvs, List<Color> colors, List<int> tris)
        {
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
