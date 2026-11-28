# SnipLite 插件接入规范（API v1）

本文描述当前开发版本的插件接入流程。插件为 .NET 10 Windows 类库，通过 [公共 SDK](../src/SnipLite.Plugin.Abstractions/PluginContracts.cs) 注册能力。插件代码在 SnipLite 进程内运行，拥有同一 Windows 用户权限。仅安装并启用来源可信的插件。

## 1. 创建项目

1. 项目目标为 `net10.0-windows`，使用 WinForms 时设置 `UseWindowsForms=true`，并设置 `EnableDynamicLoading=true` 以生成依赖信息并复制运行依赖。
2. 引用 `SnipLite.Plugin.Abstractions`。使用项目引用时设置 `Private="false"` 和 `ExcludeAssets="runtime"`。宿主提供运行时的 SDK 程序集，各插件的其他依赖在各自的加载上下文中解析。
3. 提供实现 `IPlugin` 的公共、非抽象类及公共无参构造函数。在 `Initialize(IPluginContext)` 注册能力，在 `Dispose()` 释放注册令牌及自有资源。
4. 以 [水印插件示例](../examples/SnipLite.WatermarkPlugin/) 验证外部接入，也可参考 [长截图插件](../src/SnipLite.ScrollCapture/) 的捕获、图标和窗口实现。

## 2. 清单与目录

发布包为 ZIP，可使用 `.snipplugin` 扩展名。全包须恰好包含一个 `plugin.json`。推荐将它放在包根目录，入口 DLL 与清单同目录，私有依赖及资源保留构建输出中的相对路径。

```text
example.watermark.snipplugin
├─ plugin.json
├─ SnipLite.WatermarkPlugin.dll
├─ SnipLite.WatermarkPlugin.deps.json
└─ assets/
   └─ icon.png
```

```json
{
  "id": "example.watermark",
  "name": "Watermark example",
  "version": "0.1.0",
  "apiVersion": 1,
  "minHostVersion": "0.3.15",
  "entryAssembly": "SnipLite.WatermarkPlugin.dll",
  "entryType": "SnipLite.WatermarkPlugin.WatermarkPlugin",
  "architecture": "any",
  "icon": "assets/icon.png"
}
```

| 字段 | 类型 | 约定 |
| --- | --- | --- |
| `id` | 字符串 | 全局唯一且升级时保持稳定。以 ASCII 字母或数字开头，后续可用字母、数字、点、横线和下划线，最多 96 字符。建议使用作者或组织命名空间。 |
| `name` | 字符串 | 在插件管理页显示的名称。 |
| `version` | 字符串 | 可由 .NET `Version` 解析的数字版本，例如 `0.1.0`。 |
| `apiVersion` | 整数 | SDK 主版本，当前为 `1`。 |
| `minHostVersion` | 字符串 | 插件所需最低宿主版本。省略时宿主默认使用 `0.3.14`。 |
| `entryAssembly` | 字符串 | 入口 DLL 文件名，与清单同目录。 |
| `entryType` | 字符串 | `IPlugin` 实现的完整类型名。 |
| `architecture` | 字符串 | 可选。默认 `any`，也可按宿主进程架构声明 `x64`、`arm64` 或 `x86`。 |
| `icon` | 字符串 | 可选。插件目录内的相对 PNG 路径，建议用带透明背景的 96 × 96 图标。上限为 1 MiB、1024 × 1024。 |

插件贡献的 ID 使用和插件 ID 相同的字符约定。同一个插件内所有扩展点共享命名空间，宿主用 `插件ID/贡献ID` 识别命令和保存快捷键。图标由插件提供，应用于插件列表和命令入口。可选图标缺失或解码失败时显示通用插件图标。

## 3. 注册能力

| 接口 | 注册调用 | 宿主行为 |
| --- | --- | --- |
| `PluginCommand` | `context.RegisterCommand(command)` | 从托盘、F1 工具栏或用户设置的全局快捷键执行。 |
| `ICaptureProvider` | `context.RegisterCaptureProvider(provider)` | 捕获成功后打开宿主的图片预览。 |
| `IImageProcessor` | `context.RegisterImageProcessor(processor)` | 从命令入口选图后执行，也可处理预览中的图像。 |
| `IOutputProvider` | `context.RegisterOutputProvider(provider)` | 从命令入口选图后导出，也可导出预览中的图像。 |

F1 选区中启动插件时，宿主把该物理选区交给插件的下一次 `Host.SelectRegionAsync`。插件可以用 `Host.StyleWindow` 让自己的 WinForms 窗口跟随宿主主题。命令名使用 `PluginText` 提供英文、简体和可选繁体，其他语言使用英文。插件数据写入 `context.DataDirectory`，按需创建目录。

需要让用户在命令运行期间继续截图时，可为非模态命令设置 `AllowScreenshotsWhileRunning`。调用 `Host.KeepVisibleDuringScreenshot(form)` 可让指定窗体在打开或重新激活截图编辑器时保持可见，截图编辑器保留焦点。同一插件有多个独立窗体时，请按从下到上的顺序登记。与已登记窗体及其子对话框交互时，截图编辑器保持打开。窗体释放后自动取消登记。插件负责自己的窗体定位和采集行为。

## 4. 生命周期与图像所有权

- 交互命令和提供器在 UI 线程开始。CPU 密集计算可交给后台线程。屏幕捕获 `Host.CaptureRegion` 在 UI 线程调用，返回调用方负责释放的位图。
- 长时操作传播 `CancellationToken`。宿主一次执行一个交互插件任务，停用或退出时会取消任务。停用即时撤下入口，已经加载的程序集在宿主退出时释放。
- `ICaptureProvider` 和 `IImageProcessor` 返回的 `PluginImage` 交给调用方释放。处理与输出的输入图像由宿主借出，须只读使用。处理结果需要自己的位图。
- `Host.PreviewImageAsync` 在调用期间借用图像，调用方在任务结束后释放。`SourceBounds` 是物理屏幕中的原始位置，允许负坐标。长图的像素尺寸由 `Image.Size` 表示。
- 插件退出时释放注册令牌、窗体、位图、计时器和事件订阅。宿主会在初始化失败时撤销已注册的入口。

## 5. 构建、导入与更新

```powershell
./scripts/package-plugin.ps1 -Project examples/SnipLite.WatermarkPlugin/SnipLite.WatermarkPlugin.csproj
```

把资源设置为 `CopyToOutputDirectory="PreserveNewest"`。脚本将发布文件打包到 `.local/plugin-packages`，运行时 SDK 由宿主提供。可在「首选项 → 插件」导入 ZIP 或 `.snipplugin`，也可填入 HTTPS 包直链。首次导入的第三方插件需要手动启用。更新时使用相同 ID 和更高的 `version`，直接导入新包，再点击「立即重启」使其生效。在「已安装插件」点击对应行右侧的「卸载插件」并确认。重启后移除插件，保留用户设置、数据和快捷键。

安装器先解压并校验，再把包保存到 `%LOCALAPPDATA%\SnipLite\Plugins\.packages` 下独立的版本目录，并记录下次启动要加载的包。当前正在使用的 DLL 继续工作到重启。包最多 512 个条目，下载和解压大小各限 64 MiB。拒绝符号链接、重复路径、路径越界以及相同版本或降级导入。

**随程序提供的插件。** 从 v0.3.15 起，单文件版、安装版和解压版均自带长截图插件 `sniplite.scroll-capture`。宿主通过公共插件加载器加载，默认启用，并保留明确的停用选择。通过新版插件页导入更高版本后，重启时优先加载该版本。「恢复内置版本」会移除独立更新包并切回随附版本。「卸载插件」会在重启后移除长截图的入口，可通过导入或发现插件重新安装。未主动卸载内置版时，若独立包加载失败或主程序随附的版本更新，宿主会使用随附版本。

API v1 的现有接口保持兼容。新增能力应使用可选接口，破坏性契约变更使用新的 SDK 主版本。

## 6. 独立发布与目录收录

长截图从 [独立插件仓库](https://github.com/GxDongV/SnipLite-ScrollCapture) 发布。其 Release 同时提供插件包和开发用的 `SnipLite.Plugin.Abstractions.1.0.0.nupkg`。独立开发者把 SDK 包加入本地 NuGet 源，并使用带 `ExcludeAssets="runtime"` 的 `PackageReference` 编译。运行时 SDK 仍由宿主提供。

屏幕录制在独立仓库维护，可从其 Releases 下载，也可在 SnipLite 里通过「发现插件」安装。其版本清单声明录制界面所需的最低宿主版本。[屏幕录制源码和构建说明](https://github.com/GxDongV/SnipLite-ScreenRecorder)

将版本化的 `.snipplugin` 上传到 GitHub Releases。可在自己的插件仓库发布，也可由维护者发布到 [官方插件目录](https://github.com/GxDongV/SnipLite-Plugin-Catalog)。计算已发布文件的 SHA-256，再更新官方目录。目录中的插件 ID、版本、API 版本、最低宿主版本和架构应与包内清单一致。客户端在「首选项 → 插件 → 发现插件」读取目录，下载后先校验摘要，再校对清单并安装。

## 7. 接入验收

用干净的用户配置导入、启用，检查托盘和 F1 工具栏入口、图标、关闭和取消。在「首选项 → 控制 → 全局快捷键」底部设置插件命令的快捷键，重启后核对绑定。测试处理器的输入生命周期，以及预览中的复制、保存和贴图。仓库回归可用以下命令运行。

```powershell
dotnet run --project validation/SnipLite.PluginChecks/SnipLite.PluginChecks.csproj -c Release
```
