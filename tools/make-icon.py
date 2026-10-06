"""
从 icon.svg 生成程序图标的多尺寸 PNG 与Windows ICO。

用法（在项目根目录执行）：
    python tools/make-icon.py

产物：
    src/Origami.Desktop/Assets/icon.png   256px，窗口标题图标用
    src/Origami.Desktop/Assets/icon.ico   16/24/32/48/64/128/256，Win32 资源

依赖：Microsoft Edge（或 Chrome）无头模式做 SVG 渲染。
换配色或改图案时，改 icon.svg 后重跑本脚本即可。

ICO 格式：6 字节头 + 16 字节目录项 + 各尺寸的 PNG 数据
（Vista 起允许目录项直接指向 PNG）。
"""
import os
import shutil
import struct
import subprocess
import tempfile

TMP = r"C:\Users\Administrator\AppData\Local\Temp\icongen"
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
SVG = os.path.join(TMP, "icon.svg")
ASSETS = r"G:\LLM\WorkBuddy_Space\代码编写1\origami\src\Origami.Desktop\Assets"

# Windows 会按 DPI 与场景挑选：16 任务栏、32 任务栏(大)、48/64 资源管理器、
# 128/256 高DPI 与大图标预览
SIZES = [16, 24, 32, 48, 64, 128, 256]


def render(size):
    """用 HTML 包裹渲染指定尺寸。直接渲染 SVG 在小尺寸下会被Edge 缩放丢失。"""
    html = os.path.join(TMP, f"r{size}.html")
    png = os.path.join(TMP, f"r{size}.png")
    if os.path.exists(png):
        os.remove(png)

    with open(html, "w", encoding="utf-8") as f:
        f.write(
            '<!doctype html><html><head><style>'
            f'html,body{{margin:0;padding:0;background:transparent}}'
            f'img{{width:{size}px;height:{size}px;display:block}}'
            '</style></head><body>'
            '<img src="icon.svg"></body></html>'
        )

    out_win = png.replace("\\", "\\\\").replace("/", "//")
    subprocess.run(
        [EDGE, "--headless", "--disable-gpu", "--no-sandbox",
         "--default-background-color=00000000",
         f"--screenshot={out_win}",
         f"--window-size={size},{size}",
         f"file:///{html.replace(os.sep, '/')}"],
        capture_output=True, timeout=60,
    )

    if not os.path.exists(png):
        raise RuntimeError(f"{size}px 渲染失败")
    return png


def make_ico(pngs):
    """打包成 ICO（Vista+ 允许目录项直接指向 PNG 数据）。"""
    n = len(pngs)
    # ICONDIR: reserved(2) type(2) count(2)
    header = struct.pack("<HHH", 0, 1, n)
    offset = 6 + 16 * n

    entries = b""
    data = b""
    for path in pngs:
        size = int(os.path.splitext(os.path.basename(path))[0][1:])
        with open(path, "rb") as f:
            blob = f.read()
        # ICONDIRENTRY: w,h,colorcount,reserved,planes,bitcount,bytes,offset
        entries += struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,   # 0 表示 256
            size if size < 256 else 0,
            0, 0, 1, 32, len(blob), offset,
        )
        data += blob
        offset += len(blob)

    return header + entries + data


os.makedirs(ASSETS, exist_ok=True)

paths = []
for s in SIZES:
    p = render(s)
    paths.append(p)
    print(f"  {s:>3}px -> {os.path.getsize(p):>6} bytes")

ico_path = os.path.join(ASSETS, "icon.ico")
with open(ico_path, "wb") as f:
    f.write(make_ico(paths))
print(f"ICO: {ico_path}  {os.path.getsize(ico_path)} bytes  尺寸={SIZES}")

# 只把 256px 另存为 icon.png —— 那是窗口标题图标要用的。
# 其余尺寸的中间产物留在临时目录，不进仓库（ICO 里已含）。
png_path = os.path.join(TMP, "r256.png")
shutil.copy(png_path, os.path.join(ASSETS, "icon.png"))
print(f"PNG: {os.path.join(ASSETS, 'icon.png')}")