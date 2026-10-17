# -*- coding: utf-8 -*-
"""
验证 BuildBox 修复后的网格几何正确性：
  1. 所有顶点都在包围盒 [-hx,hx]x[-hy,hy]x[-hz,hz] 内（不飞出）
  2. 每个三角形的面法线 cross(v1-v0, v2-v0) 指向背面（Unity 左手系，顺时针为正面，
     即 cross 与朝外顶点法线 n 的点积 < 0）
  3. 几何封闭（watertight）：按【顶点坐标】量化后，每条边恰好被两个三角形共享、方向相反
     （box 每个面独立顶点，故不能按顶点索引判封闭，必须按坐标）
用 hx=1.1, hy=2.75, hz=1.1, segY=40（即 BuildBox(2.2, 5.5, 2.2, 40)）。
"""
from collections import defaultdict

EPS = 1e-6


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def q(p):
    return tuple(round(c / EPS) for c in p)  # 量化坐标当 key


def build_box(sx, sy, sz, segY):
    hx, hy, hz = sx / 2.0, sy / 2.0, sz / 2.0
    verts, normals, tris = [], [], []
    sides = [
        ((1, 0, 0),   (hx, -hy, hz),  (0, 0, -2 * hz)),   # +X
        ((-1, 0, 0),  (-hx, -hy, -hz), (0, 0, 2 * hz)),   # -X
        ((0, 0, 1),   (-hx, -hy, hz),  (2 * hx, 0, 0)),   # +Z
        ((0, 0, -1),  (hx, -hy, -hz),  (-2 * hx, 0, 0)),  # -Z
    ]
    for n, origin, uaxis in sides:
        start = len(verts)
        for v in range(segY + 1):
            tv = v / segY
            for u in range(2):
                p = (origin[0] + uaxis[0] * u,
                     origin[1] + tv * 2 * hy,
                     origin[2] + uaxis[2] * u)
                verts.append(p)
                normals.append(n)
        for v in range(segY):
            a = start + v * 2
            tris.append((a, a + 2, a + 1))
            tris.append((a + 1, a + 2, a + 3))

    def flat_quad(dirv, c0, c1):
        start = len(verts)
        verts.append(c0)
        verts.append((c1[0], c0[1], c0[2]))
        verts.append(c1)
        verts.append((c0[0], c0[1], c1[2]))
        for _ in range(4):
            normals.append(dirv)
        tris.append((start, start + 1, start + 2))
        tris.append((start, start + 2, start + 3))

    flat_quad((0, 1, 0),  (-hx, hy, -hz), (hx, hy, hz))
    flat_quad((0, -1, 0), (-hx, -hy, hz), (hx, -hy, -hz))
    return verts, normals, tris


verts, normals, tris = build_box(2.2, 5.5, 2.2, 40)
hx, hy, hz = 1.1, 2.75, 1.1

# 1) 顶点越界
out = [p for p in verts if abs(p[0]) > hx + 1e-9 or abs(p[1]) > hy + 1e-9 or abs(p[2]) > hz + 1e-9]
print("顶点数 =", len(verts), "| 三角面 =", len(tris), "| 越界顶点 =", len(out))

# 2) 法线朝向：cross 应指向背面（fn·n < 0）
bad = 0
for t in tris:
    v0, v1, v2 = verts[t[0]], verts[t[1]], verts[t[2]]
    fn = cross(sub(v1, v0), sub(v2, v0))
    n = normals[t[0]]
    if dot(fn, n) >= 0:  # >= 0 说明 cross 没指向背面（绕序反/退化）
        bad += 1
print("绕序错误(法线未朝外)的三角形 =", bad)

# 3) 几何封闭性：按坐标量化后的边，每条恰 2 次、方向相反
dir_cnt = defaultdict(lambda: [0, 0])
for t in tris:
    for i in range(3):
        pa, pb = q(verts[t[i]]), q(verts[t[(i + 1) % 3]])
        if pa < pb:
            dir_cnt[(pa, pb)][0] += 1
        else:
            dir_cnt[(pb, pa)][1] += 1
open_edges = [(k, v) for k, v in dir_cnt.items() if v[0] != 1 or v[1] != 1]
print("开放边(几何不封闭)数量 =", len(open_edges))

ok = len(out) == 0 and bad == 0 and len(open_edges) == 0
print("结论:", "PASS ✅ 立方体几何封闭、法线朝外、顶点不越界" if ok else "FAIL ❌")
if open_edges:
    print("样例开放边:", open_edges[:3])
