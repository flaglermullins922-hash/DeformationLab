# -*- coding: utf-8 -*-
"""把 docs/验收截图/ 下 6 张截图拼成 2×3 总览图，供 README 与验收使用。"""
from PIL import Image, ImageDraw, ImageFont
import glob, os

SRC = r"D:\un\DeformationLab\docs\验收截图"
OUT = os.path.join(SRC, "_总览图.png")

files = ["00_original.png", "01_taper.png", "02_twist.png", "03_bend.png", "04_ffd_initial.png", "05_ffd_deformed.png"]
labels = ["原始模型", "锥化 Taper", "扭曲 Twist", "弯曲 Bend", "FFD 初始格子", "FFD 拖动控制点后"]

imgs = []
for f in files:
    p = os.path.join(SRC, f)
    if os.path.exists(p):
        imgs.append(Image.open(p))
    else:
        print("missing:", p)

if not imgs:
    raise SystemExit("no images")

cols, tw = 2, 780
th = int(imgs[0].height * tw / imgs[0].width)
lab = 40
rows = (len(imgs) + cols - 1) // cols
cv = Image.new("RGB", (cols * tw + 24 * 3, rows * (th + lab) + 24), (20, 22, 28))
dr = ImageDraw.Draw(cv)
try:
    font = ImageFont.truetype("C:/Windows/Fonts/msyhbd.ttc", 22)
except Exception:
    font = ImageFont.load_default()

for k, im in enumerate(imgs):
    rr, cc = divmod(k, cols)
    x = 24 + cc * (tw + 24)
    y = 24 + rr * (th + lab)
    cv.paste(im.resize((tw, th), Image.LANCZOS), (x, y))
    dr.text((x + 6, y + 4), labels[k], fill=(230, 240, 245), font=font)

cv.save(OUT)
print("总览图已生成:", OUT, cv.size)
