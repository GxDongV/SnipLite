# 多语言适配模块

界面本地化位于 `src/SnipLite/Localization/`，不依赖 OCR 引擎或在线翻译服务。所有语言资源随程序嵌入，离线可用；不增加第三方运行依赖。

## 语言策略

- 新设置的 `DisplayLanguage` 为 `system`，跟随当前用户的 Windows 显示语言，不使用区域格式推断语言。
- 支持 `en-US`、`zh-CN`、`zh-TW`、`ja`、`ko`、`fr`、`de`、`es`。英语各地区匹配英文，其余已支持语言按语言族匹配。
- 中文优先尊重 `Hans` / `Hant` 脚本；台湾、香港、澳门默认繁体，其他中文默认简体。未支持的系统语言回退英文。
- 手动指定的语言持久保存。旧设置中的 `zh-CN` / `en-US` 保持原意；缺失、空值和无效设置使用跟随系统。
- 启动时解析系统语言；收到 Windows 设置变更广播时重新检查。Windows 自身可能要求重新登录后才应用显示语言。
- 切换界面语言不改变 OCR 语言、OCR 模型、翻译服务或翻译方向，也不会修改用户文字、识别结果、二维码和文件路径。八语言界面不代表本地增强 OCR 支持八语言识别。

## 接口与资源

`LanguageService` 负责解析、加载资源、格式化和语言变更通知。`LanguageMonitor` 只处理系统设置广播。`LocalizedControls` 保留静态控件的原始文案，使重复切换可逆，并跳过文本编辑器和数值编辑器。

`UiText` 是现有界面的薄适配入口。新界面可直接使用 `LanguageService.Get(key)`；兼容界面使用 `UiText.T(source)`。动态文字使用 `LanguageService.Format(source, args)`，不要先拼接数据再查询资源。需要随语言重新显示的状态使用延迟文案回调，并在窗口关闭时取消语言事件订阅。

`Resources/sources.json` 将现有源文案映射到稳定资源 ID，每种语言的 JSON 保存同一组 ID。ID 不随译文修改而改变；缺少译文时回退英文。新增或改写源文案时同步维护映射和八种语言资源，必要时保留旧源文案到同一 ID 的别名。只在应用界面文字的边界调用本地化接口，不能对任意用户内容调用。

首选项会重新构建当前页面，托盘与贴图菜单随设置刷新，截图及翻译界面更新应用状态和提示，保留当前图片、编辑内容和识别结果。语言选择菜单使用各语言自身名称。系统原生文件、字体等对话框的系统按钮继续由 Windows 决定语言。

## README 语言导航

`docs/languages.json` 独立管理 README 文档路径与语言名称。各 README 中的 `languages:start` / `languages:end` 标记之间是统一生成的导航，不维护八份手写链接。

```powershell
.\scripts\update-readme-languages.ps1
.\scripts\check-localization.ps1
```

检查脚本验证资源覆盖、格式占位符、文件类型过滤器和 README 导航是否同步。翻译文件采用 UTF-8。多语言初稿包含机器翻译，核心菜单术语另行校正；欢迎母语使用者改进表述。
