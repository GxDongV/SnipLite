# 包体优化实测记录

日期：2026-11-16。工作分支：`codex/package-size-optimization`，对照提交 `a2c8e53a55c13231863e33190ffbb89ca1471bc1`（v0.3.13）。本轮优化纳入 v0.3.14。下文保留版本号仍为 0.3.13 的实验构建测量及验证记录，避免把历史校验码当成正式包校验码；正式包以 v0.3.14 Release 中的 `SHA256SUMS.txt` 为准。

## 已实现的优化

1. 保留 SimdPaddleOCR 2.0.0、PP-OCRv6 Small 和文本方向模型。截图、贴图、文字识别快捷键、翻译均使用同一本地识别入口；引擎失败会显示错误，不再静默变为空文本。
2. 删除 Windows OCR 的实现、预热和设置选项，旧配置字段保留兼容；移除九份资源文件中三个废弃条目，八语言 README 和首选项预览同步更新。
3. 保持 `net10.0-windows10.0.19041.0`、最低系统声明及安装器系统下限不变，通过 `IncludeWindowsSDKRefFrameworkReferences=false` 取消已经不再使用的 Windows SDK .NET 投影。
4. 自动框选使用 Windows 自带 UI Automation COM 服务，保留 Win32 和 MSAA 检测。新增 Interop.UIAutomationClient 10.19041.0（151,040 字节，MIT）作为 COM 声明；移除 `UseWPF`，WPF 组件由 SDK 自然退出发布。
5. COM 查询只在 MTA 工作线程执行，全局最多两个并发查询；连接/事务设置超时，遍历限定节点数、深度和时间预算，缓存矩形属性，优先深入命中分支，结束后释放本次持有的 COM 包装对象。时间预算为软限制，不能中断正在执行的提供者调用。
6. 发布脚本增加独立输出根目录，并清理对应发布叶子目录，防止旧版大 DLL 残留。保留 .NET、WinForms、模型、文档及许可证，不启用裁剪或 AOT，不做发布后 DLL 删除。
7. 同步修复手动复制：工具栏复制及 Ctrl+C 显式标记 `CaptureAction.Copy`，不再受“截图后自动复制”关闭的影响。

## 三种包的体积

所有单位为 MiB（1,048,576 字节）；阶段 1 只移除 Windows OCR/SDK 投影，阶段 2 再替换 UIA 并解除 WPF。目录包含文档与许可证。最终数值见下表及方案文档回填表。

<!-- measurements:start -->
| 项目 | v0.3.13 | 阶段 1 | 最终候选（阶段 2） | 减少 MiB | 减少比例 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 单文件 EXE | 103.99 | 97.97 | 78.75 | 25.25 | 24.28% |
| 安装 EXE | 79.54 | 76.06 | 61.61 | 17.92 | 22.54% |
| 解压 ZIP | 100.84 | 94.52 | 74.55 | 26.29 | 26.07% |
| 便携目录 | 213.66 | 189.42 | 144.15 | 69.50 | 32.53% |

两次最终构建的 267 个目录文件内容全部一致；单文件和 ZIP 的大小与 SHA256 均一致。安装 EXE 第二次为 64,604,168 字节，比第一次少 4 字节，安装器元数据导致的压缩差异不能视为逐字节复现。单文件清单包含 209 项，旧版为 243 项。
<!-- measurements:end -->

收益主要来自退出 Windows SDK/WinRT 和 WPF 的运行依赖。保留 SDK 发布闭包中的小型 WindowsBase 转发程序集，不以文件名判断是否应删除。.NET 基础运行时和 Windows Forms 仍需随自包含包分发。

## 文件核对

- 对旧版、阶段 1、阶段 2 和重复构建导出完整文件清单、大小、SHA256 和 .NET 单文件清单。
- 解压 ZIP 内每个文件与发布目录逐一核对内容；单文件中的程序集与目录版逐一核对哈希。`.deps.json` 和 `.runtimeconfig.json` 由 SDK 针对部署形态重写，分别解析并检查依赖。
- OCR 引擎、模型提供器、ChineseV6Small 和 TextLineOrientation 四个程序集的哈希与旧版一致，模型没有替换、量化或删减。
- 新包不含 `Microsoft.Windows.SDK.NET.dll`、`WinRT.Runtime.dll`、Presentation 系列或 WPF UIAutomation 客户端；不含测试、视频、本地输出、日志、PDB 和编译中间目录。
- 第三方声明及新增适配器 MIT 许可已随包提供。每次打包生成 `SHA256SUMS.txt`。
- 两个独立目录重复构建：比较所有发布文件的内容，包的压缩结果及元数据差异单独记录，不把同样大小当成相同内容。

## 已完成验证

| 检查 | 结果与边界 |
| --- | --- |
| 正式 Release 构建 | 0 警告、0 错误 |
| 严格 JSON、本地化和 README 导航 | 8 种语言各 407 条资源；另外 3,452 项语言与控件断言通过，布局候选溢出 0 项 |
| 桌面完整回归 | 截图、OCR、二维码、标注、文字编辑、首选项、悬停菜单及框选等 172 项通过 |
| 自动框选 | 直接 COM 测试绕过 Win32/MSAA 回退，验证遮罩下按钮、面板、重复查询、无效 HWND、UI 线程保护；完整遮罩流程验证禁用按钮、停留重试和单击选择 |
| OCR | 浅色、深色中英混排与空白图通过，字符坐标及旧配置字段反序列化通过；属于功能烟测，不是通用识别准确率评测 |
| 翻译 | 82 项通过，含取消、过期请求、提供者切换、错误和覆盖译文；使用测试响应，未重测第三方线上服务 |
| 主题、选区、贴图 | 分别 24、25、9 项通过，含放大镜、F3 当前选区贴图 |
| 自包含回归 | 验证器也关闭 WPF 和 SDK 投影，自包含发布后完整桌面自检及翻译测试通过；不能替代干净系统验收 |
| 手动复制 | 独立消费进程读取真实系统剪贴板，16 项通过：自动复制开/关 × PNG/位图 × 工具栏/Ctrl+C/Enter/双击配置动作；测试后恢复剪贴板 |

测试代码、生成的图片、日志和输入全部留在忽略目录。首选项八语言预览由当前界面实际渲染，已人工检查简体中文预览。

## OCR 耗时

同一台主机，10 个新进程，每进程识别同一张 680 × 145 中英混排生成图，随后立即再次识别。未清空操作系统磁盘缓存；“首次”仅表示该进程尚未加载模型。十次文字、几何及重复结果检查均通过。

| 指标 | 中位数 | 范围 |
| --- | ---: | ---: |
| 进程内首次 OCR | 373.96 ms | 371.67–376.13 ms |
| 紧接着再次 OCR | 65.16 ms | 63.48–66.29 ms |

这是候选版的功能与耗时记录，没有同条件旧版系列数据，因此不宣称 OCR、启动速度或内存得到提升。

## 尚未完成的验收

- 没有干净的 Windows 10/11 虚拟机，未做最低版本实机验证、安装/升级/卸载、开机启动及管理员启动任务验收。
- 未完成三种正式入口的冷/热启动系列、实际单文件缓存目录占用和稳定运行内存对照，不以清单推算代替运行测量。
- 自动框选已有生成控件夹具回归，尚未完成浏览器、Electron、WinUI 等真实应用的逐点新旧对照、故障提供者及长时间资源增长验收。冷 UIA 提供者可能先返回面板，再由现有停留重试细化。
- 八语言布局和已有主题/DPI 测试通过；未完成所有多显示器、混合 DPI、输入法的人工矩阵。

上述项目没有计入已通过的验证。v0.3.14 的发布说明保留这些验证边界，不宣称完成全部系统兼容验收或性能对照。

## 复现与本地产物

```powershell
dotnet build SnipLite.slnx -c Release --nologo
./scripts/check-localization.ps1
./scripts/release.ps1 -OutputRoot "$PWD/.local/package-size-optimization/stage2-packages" -IsccPath "$PWD/.local/tools/inno/ISCC.exe"
```

候选产物：`.local/package-size-optimization/stage2-packages/release/0.3.13/`，目录运行入口：`.local/package-size-optimization/stage2-packages/publish/win-x64/self-contained/SnipLite.exe`。启动前先从托盘退出当前实例，避免单实例保护阻止候选版启动。

完整测量证据：`.local/package-size-optimization/measurements.json`、`*-inventory.csv`、`*-bundle.csv`、`removed-files.csv`；性能记录在 `stage2/performance/`；回归结果在 `stage2/checks/`、`stage2/runtime-checks/` 和 `copy-fix/checks/`。这些验证数据不提交 Git。

最终候选字节数与 SHA256：

| 文件 | 字节数 | SHA256 |
| --- | ---: | --- |
| SnipLite-0.3.13-win-x64-portable.zip | 78171986 | ee1087980656362a6e0245557ff0c9930b86eff2a9e4436b2ef1875af5a13caa |
| SnipLite-0.3.13-win-x64-setup.exe | 64604172 | 9cc1aa9356569b6e6b421b20bbdd85da73060863bad13f67a861b744636dfbc6 |
| SnipLite-0.3.13-win-x64-single.exe | 82570372 | 507d38c772fe6e73521987df63a2bfc42f955551c169e4798d0c7780ed1cacff |

## v0.3.14 正式发布包

| 版本 | v0.3.13 | v0.3.14 | 减少 |
| --- | ---: | ---: | ---: |
| 单文件 EXE | 103.99 MiB | 78.75 MiB | 24.28% |
| 安装 EXE | 79.54 MiB | 61.61 MiB | 22.54% |
| 解压 ZIP | 100.84 MiB | 74.55 MiB | 26.07% |

正式构建的版本资源为 0.3.14.0，交付程序集与本次自包含回归所用程序集哈希一致；OCR 四个程序集与旧版哈希一致。正式下载及校验文件见 [v0.3.14 Release](https://github.com/GxDongV/SnipLite/releases/tag/v0.3.14)。
