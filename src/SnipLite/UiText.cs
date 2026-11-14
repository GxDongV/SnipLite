namespace SnipLite;

internal static class UiText
{
    private static readonly Dictionary<string, string> English = new()
    {
        ["首选项"]="Preferences", ["语言与识别"]="Language and recognition", ["启动与运行"]="Startup and behavior", ["配置与日志"]="Configuration and logs",
        ["跟随 Windows · 浅色"]="Windows theme · Light", ["跟随 Windows · 深色"]="Windows theme · Dark", ["跟随 Windows · 高对比度"]="Windows theme · High contrast",
        ["界面与图标跟随 Windows 浅色 / 深色模式，切换后即时生效。"]="The interface and icons follow the Windows light / dark theme automatically.",
        ["翻译服务"]="Translation service", ["配置微软密钥"]="Microsoft API key",
        ["必应网页翻译"]="Bing Translator",
        ["识别引擎"]="OCR engine", ["本地增强（中英自动识别）"]="Local enhanced (Chinese / English)", ["Windows 系统识别"]="Windows OCR", ["系统识别语言"]="Windows OCR language",
        ["GitHub 项目"]="GitHub project", ["问题反馈"]="Report an issue", ["开源许可证"]="License", ["发布版本"]="Releases",
        ["常规"]="General", ["界面"]="UI", ["截屏"]="Capture", ["贴图"]="Pin", ["输出"]="Output", ["控制"]="Control", ["更新"]="Update", ["关于"]="About",
        ["显示"]="Display", ["行为"]="Behavior", ["按键"]="Keys", ["转换"]="Conversion", ["文件"]="Files", ["剪贴板"]="Clipboard", ["鼠标"]="Mouse", ["全局快捷键"]="Global hotkeys",
        ["恢复默认"]="Restore defaults", ["显示语言"]="Display language", ["识别语言"]="OCR language", ["简体中文"]="Simplified Chinese", ["跟随系统"]="System default",
        ["开机启动"]="Start with Windows", ["以管理员身份开机启动"]="Start elevated", ["自动备份配置文件"]="Back up settings", ["保持快速响应"]="Keep responsive", ["增强版托盘菜单"]="Extended tray menu",
        ["日志级别"]="Log level", ["关闭"]="Close", ["仅错误"]="Errors only", ["普通"]="Normal", ["调试"]="Debug", ["打开日志文件"]="Open log", ["配置文件"]="Settings file",
        ["打开所在文件夹"]="Show in folder", ["打开"]="Open", ["更改"]="Change", ["界面字体"]="Interface font", ["主题颜色"]="Accent color", ["托盘图标"]="Tray icon", ["自动"]="Automatic", ["彩色"]="Color", ["自定义"]="Custom", ["选择颜色"]="Choose color",
        ["选择图标文件…"]="Choose icon…", ["放大镜"]="Magnifier", ["颜色值"]="Color format", ["隐藏 HEX 颜色值开头的 #"]="Omit leading # from HEX colors", ["截图翻译"]="Translate screenshot", ["Hex 颜色值大写"]="Uppercase HEX", ["显示操作提示"]="Show hints", ["选区前自动显示放大镜"]="Automatic magnifier", ["放大镜倍数"]="Magnification",
        ["边框宽度"]="Border width", ["遮罩颜色"]="Mask color", ["遮罩不透明度"]="Mask opacity", ["显示锚点"]="Show handles", ["锚点描边颜色"]="Handle color", ["放大镜中显示遮罩"]="Mask in magnifier", ["放大镜中显示边框"]="Border in magnifier", ["放大镜中显示锚点"]="Handles in magnifier", ["显示全屏十字线"]="Fullscreen crosshair", ["显示快捷键提示"]="Show shortcut hints",
        ["自动检测窗口"]="Detect windows", ["自动检测界面元素"]="Detect UI elements", ["隐藏 1×1 提示"]="Hide 1×1 hint", ["捕捉鼠标指针"]="Capture cursor", ["完全禁用鼠标指针捕捉"]="Disable cursor capture", ["其他程序窗口激活时退出截屏"]="Cancel when another app activates", ["最大历史截屏区域数"]="Region history limit", ["循环历史区域"]="Wrap region history", ["最大历史记录数"]="Image history limit", ["截图后自动复制"]="Copy after capture", ["音效文件"]="Sound file", ["试听"]="Preview sound",
        ["禁用鼠标滚轮功能"]="Disable capture wheel", ["按 Esc 时弹出确认对话框"]="Confirm on Escape", ["忽略其他程序模拟的复制命令"]="Ignore simulated copy keys", ["截屏时屏蔽左 Win 键"]="Block left Windows key", ["截屏时屏蔽右 Win 键"]="Block right Windows key", ["左键双击"]="Double-click", ["中键单击"]="Middle-click", ["回车键"]="Enter", ["复制到剪贴板"]="Copy", ["贴到屏幕"]="Pin", ["快速保存"]="Quick save", ["无操作"]="No action", ["然后退出截屏"]="Exit afterwards",
        ["窗口阴影"]="Window shadow", ["阴影颜色"]="Shadow color", ["默认不透明度"]="Default opacity", ["最大窗口尺寸"]="Max window size", ["快速缩略图宽度"]="Thumbnail width", ["快速缩略图高度"]="Thumbnail height", ["在右键菜单显示助记键"]="Show menu shortcuts", ["使用截屏时的位置"]="Use capture position", ["使用鼠标滚轮缩放"]="Wheel zoom", ["以鼠标指针为中心缩放"]="Zoom at cursor", ["平滑缩放"]="Smooth scaling", ["缩略图接受重置操作"]="Reset thumbnails", ["边框闪烁时使窗口半透明"]="Translucent while flashing", ["切换分组时自动闪烁贴图"]="Flash on group switch", ["闪烁时按贴图键隐藏所有贴图"]="Pin key hides flashing images", ["拖放操作时保留原始图"]="Keep source after drag", ["贴出新图片时闪烁"]="Flash new pin", ["贴出新图片时激活"]="Activate new pin", ["可恢复的贴图数"]="Recoverable pins",
        ["文字转图片"]="Text to image", ["总是忽略 HTML 格式"]="Ignore HTML formatting", ["不把文件路径转化成贴图"]="Ignore file paths", ["页边距"]="Padding", ["图片最大宽度"]="Max image width", ["字体"]="Font", ["背景颜色"]="Background", ["文本颜色"]="Text color", ["预览文字贴图"]="Preview text image",
        ["JPEG 图像质量"]="JPEG quality", ["文件名"]="File name", ["预览"]="Preview", ["记住上一次使用的图片扩展名"]="Remember image extension", ["快速保存时显示通知"]="Quick save notification", ["快速保存路径"]="Quick save folder", ["自动保存"]="Auto save", ["自动保存路径"]="Auto save folder", ["启用全局快捷键"]="Enable global hotkeys",
        ["隐藏 / 显示所有贴图"]="Hide / show pins", ["切换到另一贴图分组"]="Switch pin group", ["鼠标穿透开关"]="Toggle click-through", ["文字识别"]="OCR", ["二维码识别"]="Scan QR code", ["截屏并自动复制"]="Capture and copy", ["自定义截屏…"]="Custom capture…", ["贴图 / 恢复贴图"]="Pin / restore", ["恢复最近关闭的贴图"]="Restore closed pin", ["关闭全部贴图"]="Close all pins", ["截屏历史"]="Capture history", ["清空截屏历史"]="Clear capture history", ["禁用快捷键"]="Disable hotkeys", ["首选项…"]="Preferences…", ["帮助"]="Help", ["重新启动"]="Restart", ["退出"]="Exit"
    };
    public static string T(string text, string language) => language == "en-US" ? English.GetValueOrDefault(text, text) : text;
    public static void Apply(Control root, string language)
    {
        root.Text = T(root.Text, language);
        if (root is ToolStrip strip) foreach (ToolStripItem item in strip.Items) ApplyItem(item, language);
        foreach (Control child in root.Controls) Apply(child, language);
    }
    private static void ApplyItem(ToolStripItem item, string language)
    {
        item.Text = T(item.Text ?? "", language);
        if (item is ToolStripDropDownItem parent) foreach (ToolStripItem child in parent.DropDownItems) ApplyItem(child, language);
    }
}
