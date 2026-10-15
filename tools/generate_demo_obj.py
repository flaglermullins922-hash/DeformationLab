# -*- coding: utf-8 -*-
"""生成 DeformationLab 的演示 OBJ 示例文件（放到 StreamingAssets/models/）
供"导入 obj 文件"功能演示使用。
- bipyramid.obj   ：细长双锥（八边形赤道 + 上下锥），变形（锥化/扭曲/弯曲）最明显
- icosahedron.obj ：正二十面体（20 面），FFD 自由变形演示效果好
"""
import math, os

OUT = r"D:\un\DeformationLab\Assets\StreamingAssets\models"
os.makedirs(OUT, exist_ok=True)


def write_obj(path, name, verts, faces):
    with open(path, "w", encoding="utf-8") as f:
        f.write("# %s - generated demo model for DeformationLab\n" % name)
        f.write("o %s\n" % name)
        for v in verts:
            f.write("v %.6f %.6f %.6f\n" % (v[0], v[1], v[2]))
        for face in faces:
            f.write("f " + " ".join(str(i + 1) for i in face) + "\n")
    print("written:", path, " verts=%d faces=%d" % (len(verts), len(faces)))


# ---- 细长双锥（八边形赤道，沿 Y 轴拉长，半径 1.5，高 8）----
verts = [(0.0, 4.0, 0.0), (0.0, -4.0, 0.0)]
for i in range(8):
    a = i * math.pi / 4.0
    verts.append((1.5 * math.cos(a), 0.0, 1.5 * math.sin(a)))
faces = []
for i in range(8):
    e0 = 2 + i
    e1 = 2 + ((i + 1) % 8)
    faces.append((0, e0, e1))          # 上锥
    faces.append((1, e1, e0))          # 下锥
write_obj(os.path.join(OUT, "bipyramid.obj"), "bipyramid", verts, faces)

# ---- 正二十面体（外接球半径 3）----
t = (1.0 + math.sqrt(5.0)) / 2.0
r = 3.0 / math.sqrt(1.0 + t * t)
icov = [
    (-r, t * r, 0), (r, t * r, 0), (-r, -t * r, 0), (r, -t * r, 0),
    (0, -r, t * r), (0, r, t * r), (0, -r, -t * r), (0, r, -t * r),
    (t * r, 0, -r), (t * r, 0, r), (-t * r, 0, -r), (-t * r, 0, r),
]
icof = [
    (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
    (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
    (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
    (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
]
write_obj(os.path.join(OUT, "icosahedron.obj"), "icosahedron", icov, icof)
