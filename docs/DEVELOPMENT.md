# 开发说明

## 环境

使用 Windows、.NET 10 SDK 和交互式桌面。UI 使用 WinForms，自绘部分采用 GDI+；窗口与控件检测使用 Win32、MSAA 和原生 UI Automation COM；OCR 通过 SimdPaddleOCR + PP-OCRv6 Small，扫码通过 ZXing.Net。保留文本方向模型，不再使用 Windows OCR、Windows SDK/WinRT 投影或 WPF 自动化客户端。

版本统一在 `src/SnipLite/SnipLite.csproj` 的 `Version` 中维护，正式程序从程序集元数据读取版本。

## 构建

```powershell
dotnet build SnipLite.slnx -c Release
dotnet run --project src/SnipLite/SnipLite.csproj -c Release --no-build
```

正式程序支持 `--capture`（启动后截图）、`--prefs` / `--prefs:N`（打开首选项指定页，N 为 0–7）、`--menu`（打开托盘菜单）。启动新版前请从托盘退出旧实例。

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
| 界面语言适配 | `Localization/LanguageService.cs`、`Localization/LocalizedControls.cs`、`Localization/LanguageMonitor.cs` |

## 多语言文档与资源

README 面向使用者，介绍功能、下载和操作。语言资源、系统语言匹配策略与 README 导航维护方式见 [多语言适配说明](LOCALIZATION.md)。

```powershell
.\scripts\update-readme-languages.ps1
.\scripts\check-localization.ps1
```

## 发布与清理

```powershell
.\scripts\publish.ps1
.\scripts\publish.ps1 -SelfContained
.\scripts\publish.ps1 -SingleFile
.\scripts\release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
.\scripts\clean.ps1
```

发布脚本仅发布正式项目，产物位于 `artifacts/publish/win-x64/`。提交源码时排除 `bin/`、`obj/`、`artifacts/`、测试输出和本地参考材料；这些目录已配置在 `.gitignore` 中。

实验构建可给 `publish.ps1` 或 `release.ps1` 传入 `-OutputRoot "$PWD/.local/package-size-optimization/candidate"`。每次发布会清空该根目录下对应的 `publish/win-x64/<形态>`，避免遗留旧依赖；不会清空输出根目录或其他形态。需要保留旧包时使用新的输出根目录。

第三方许可证的来源与版本见 `licenses/README.md`。更新依赖或 .NET 运行时版本时，同步核对对应的声明。

`release.ps1` 为同一版本生成独立运行的单文件 EXE、Inno Setup 安装 EXE、解压 ZIP 及 `SHA256SUMS.txt`。安装版按当前用户安装，卸载时保留用户设置和自行添加的文件。正式程序内嵌许可证，单文件模式也可在帮助页离线查看。
