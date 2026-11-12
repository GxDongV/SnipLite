# SnipLite logo

`SnipLite.svg` 是正式 logo 的透明背景矢量源文件，沿用手绘方案的蓝色四角框（`#1764DD`）和绿色斜线（`#087A61`）。

- `SnipLite.png`：512 × 512 透明背景预览。
- `SnipLite.ico`：16、20、24、32、40、48、64、128、256 像素的 Windows 图标，用于程序文件、默认托盘、窗口和安装程序。

修改 SVG 后，在仓库根目录运行 `powershell -ExecutionPolicy Bypass -File scripts/generate-logo.ps1` 重新生成 PNG 和 ICO，再构建程序。生成脚本支持当前 SVG 使用的圆头、圆角折线。
