# 开发说明

## 环境

使用 Windows、.NET 10 SDK 和交互式桌面。UI 使用 WinForms，自绘部分采用 GDI+；窗口与控件检测使用 Win32、MSAA 和 UI Automation；OCR 默认通过 SimdPaddleOCR + PP-OCRv6 Small，Windows.Media.Ocr 备用，扫码通过 ZXing.Net。

版本统一在 `src/SnipLite/SnipLite.csproj` 的 `Version` 中维护，正式程序从程序集元数据读取版本。

## 构建

```powershell
dotnet build SnipLite.slnx -c Release
dotnet run --project src/SnipLite/SnipLite.csproj -c Release --no-build
```

正式程序支持 `--capture`（启动后截图）、`--prefs` / `--prefs:N`（打开首选项指定页，N 为 0–7）、`--menu`（打开托盘菜单）。启动新版前请从托盘退出旧实例。

## 回归检查

```powershell
dotnet run --project tests/SnipLite.SelfTest -c Release -- --selftest
```

这是独立的桌面诊断程序，并非 `dotnet test` 测试项目。它会短暂显示测试窗口、读取当前屏幕，并验证截图、OCR / 扫码、标注、文字再编辑、原生控件及遮罩下的自动框选等行为。需要可用的交互式桌面；检查 Windows OCR 时另需系统语言包，不适合直接在无桌面的 CI 环境中运行。

输出默认放在 `%TEMP%\SnipLite.SelfTest\`。通过 `SNIPLITE_TEST_OUTPUT` 可指定其他目录。输出包含测试报告和可能带有当前桌面内容的图片，不应提交到仓库。

只运行相关检查：

```powershell
dotnet run --project tests/SnipLite.SelfTest -c Release -- --menu-test
dotnet run --project tests/SnipLite.SelfTest -c Release -- --target-test
dotnet run --project tests/SnipLite.SelfTest -c Release -- --text-test
dotnet run --project tests/SnipLite.SelfTest -c Release -- --translation-test
```

诊断入口还包括 `--demo`、`--annotation-demo`、`--preferences-demo`、`--feedback-demo`、`--inline-demo`、`--recognition-demo` 与 `--result-demo`，用于渲染样例或交互检查。正式程序中不包含这些入口。

自动检查覆盖特定场景，不能代替多屏、混合 DPI、输入法和第三方应用的实际验收。

OCR 的小范围实用性检查可运行 `.\scripts\test-ocr.ps1`，首次下载 4 张固定版本、SHA-256 校验的公开样例到被忽略的 `.local/`，随后可加 `-SkipDownload` 使用缓存。结果保存到 `artifacts/ocr-smoke/ocr-smoke.json`。检查另含本地生成的浅色/深色中英混排与空白图，不把小样本通过率当作通用准确率。增强引擎文本不符（包括空白误报）、推理异常、越界字符框或重复结果不稳定会返回失败；Windows 引擎文本仅作对照记录。详见 [OCR 接入与后续翻译](OCR_TRANSLATION_PLAN.md)。

`--translation-test` 检查 OCR 边缘命中、保留完整工具栏的 OCR 状态行与标注工具切换，以及翻译响应解析、失败提示、单行语言栏、多 DPI 布局、取消、过期结果隔离、背景采样、透明层渲染、原文逐像素恢复和标注/导出。默认用模拟网络响应，并只对自行绘制的选区文字执行 OCR。加 `--live` 会把该生成样例的英文发送到 Google，检查 OCR → 在线翻译 → 原位覆盖；不发送当前桌面文字。OCR 状态行、覆盖效果、完整工具栏与语言菜单预览也输出到测试目录。

## 代码导航

| 模块 | 文件 |
| --- | --- |
| 程序入口、托盘与热键 | `Program.cs`、`TrayContext.cs`、`HotkeyHost.cs` |
| 截图与自动选区 | `CaptureForm.cs`、`DesktopTargets.cs`、`CaptureCrosshair.cs` |
| 标注与文字编辑 | `Annotation.cs`、`AnnotationBar.cs`、`TextEditLayout.cs`、`ImeSink.cs` |
| OCR 与扫码 | `Services.cs`、`SimdOcr.cs`、`OcrSelection.cs`、`RecognitionResultForm.cs` |
| 截图翻译 | `CaptureTranslation.cs`、`TranslationPanel.cs`、`TranslationOverlay.cs`、`TranslationService.cs` |
| 贴图 | `PinForm.cs`、`PinShadowWindow.cs`、`TextImageRenderer.cs` |
| 首选项与持久化 | `PreferencesForm.cs`、`PreferencesPages.cs`、`AppSettings.cs`、`PreferenceSettings.cs` |

## 发布与清理

```powershell
.\scripts\publish.ps1
.\scripts\publish.ps1 -SelfContained
.\scripts\publish.ps1 -SingleFile
.\scripts\release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
.\scripts\clean.ps1
```

发布脚本仅发布正式项目，产物位于 `artifacts/publish/win-x64/`。提交源码时排除 `bin/`、`obj/`、`artifacts/`、测试输出和本地参考材料；这些目录已配置在 `.gitignore` 中。

第三方许可证的来源与版本见 `licenses/README.md`。更新依赖或 .NET 运行时版本时，同步核对对应的声明。

`release.ps1` 为同一版本生成独立运行的单文件 EXE、Inno Setup 安装 EXE、解压 ZIP 及 `SHA256SUMS.txt`。安装版按当前用户安装，卸载时保留用户设置和自行添加的文件。正式程序内嵌许可证，单文件模式也可在帮助页离线查看。
