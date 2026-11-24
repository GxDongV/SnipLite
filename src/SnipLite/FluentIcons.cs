using System.Drawing.Drawing2D;

namespace SnipLite;

/// <summary>Resolution-independent 24-unit line icons shared by navigation, menus and capture tools.</summary>
internal static class FluentIcons
{
    public static bool Supports(string id) => id is "rect" or "ellipse" or "arrow" or "pen" or "marker" or "eraser" or "text" or "mosaic" or "ocr" or "qr" or "translate" or "undo" or "redo" or "close" or "pin" or "save" or "copy";

    public static void Draw(Graphics g, string id, RectangleF bounds, Color color)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
            using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            void Line(float x1, float y1, float x2, float y2) => g.DrawLine(pen, x1, y1, x2, y2);
            void Lines(params PointF[] points) => g.DrawLines(pen, points);
            void Box(float x, float y, float w, float h, float r = 2) { using var path = FluentDrawing.Rounded(new RectangleF(x, y, w, h), r); g.DrawPath(pen, path); }
            void DownArrow(float x, float top)
            {
                Line(x, top, x, top + 8);
                Lines(new(x - 3, top + 5), new(x, top + 8), new(x + 3, top + 5));
            }
            void Corners()
            {
                Lines(new(8, 3), new(3, 3), new(3, 8)); Lines(new(16, 3), new(21, 3), new(21, 8));
                Lines(new(3, 16), new(3, 21), new(8, 21)); Lines(new(16, 21), new(21, 21), new(21, 16));
            }
            switch (id)
            {
                case "chevron": Lines(new(7, 10), new(12, 15), new(17, 10)); break;
                case "rect": Box(4, 5, 16, 14); break;
                case "ellipse": g.DrawEllipse(pen, 4, 5, 16, 14); break;
                case "arrow": Line(5, 19, 19, 5); Lines(new(10, 5), new(19, 5), new(19, 14)); break;
                case "pen": Lines(new(4, 16), new(15, 5), new(19, 9), new(8, 20), new(3, 21), new(4, 16)); Line(13, 7, 17, 11); break;
                case "marker": Lines(new(4, 14), new(15, 3), new(21, 9), new(10, 20), new(4, 14)); Line(3, 21, 10, 21); Line(6, 12, 12, 18); break;
                case "eraser": Lines(new(4, 13), new(13, 4), new(21, 12), new(12, 21), new(8, 21), new(4, 17), new(4, 13)); Line(8, 9, 16, 17); Line(12, 21, 21, 21); break;
                case "text": Line(4, 5, 20, 5); Line(12, 5, 12, 20); Line(8, 20, 16, 20); break;
                case "mosaic": Box(4, 4, 16, 16); Line(12, 4, 12, 20); Line(4, 12, 20, 12); break;
                case "ocr": Corners(); Line(7, 8, 17, 8); Line(12, 8, 12, 17); Line(9, 17, 15, 17); break;
                case "qr": Box(3, 3, 6, 6, 1); Box(15, 3, 6, 6, 1); Box(3, 15, 6, 6, 1); Lines(new(15, 14), new(15, 18), new(19, 18), new(19, 21)); Line(21, 14, 21, 16); Line(14, 21, 16, 21); break;
                case "translate": Line(3, 5, 15, 5); Line(9, 2, 9, 5); g.DrawArc(pen, 4, 1, 9, 12, 0, 90); Line(6, 8, 12, 14); Lines(new(12, 21), new(17, 10), new(22, 21)); Line(14, 17, 20, 17); break;
                case "undo": Lines(new(8, 4), new(3, 9), new(8, 14)); Lines(new(3, 9), new(14, 9), new(18, 12), new(18, 17), new(15, 20), new(10, 20)); break;
                case "redo": Lines(new(16, 4), new(21, 9), new(16, 14)); Lines(new(21, 9), new(10, 9), new(6, 12), new(6, 17), new(9, 20), new(14, 20)); break;
                case "close": Line(6, 6, 18, 18); Line(18, 6, 6, 18); break;
                case "minimize": Line(6, 12, 18, 12); break;
                case "maximize": Box(6, 6, 12, 12, 1); break;
                case "restore": Box(5, 8, 11, 11, 1); Lines(new(8, 5), new(19, 5), new(19, 16)); break;
                case "pin": Lines(new(9, 3), new(19, 13), new(15, 14), new(11, 18), new(6, 13), new(10, 9), new(9, 3)); Line(8, 16, 3, 21); break;
                case "save": Lines(new(4, 3), new(17, 3), new(21, 7), new(21, 21), new(3, 21), new(3, 4), new(4, 3)); Box(7, 3, 9, 6, 1); Box(7, 14, 10, 7, 1); break;
                case "copy": Box(8, 8, 13, 13); Lines(new(16, 4), new(16, 3), new(3, 3), new(3, 16), new(4, 16)); break;
                case "capture": Corners(); Line(9, 15, 15, 9); break;
                case "zoom-in":
                case "zoom-out":
                    g.DrawEllipse(pen, 3, 3, 13, 13); Line(14, 14, 21, 21); Line(6, 9.5f, 13, 9.5f);
                    if (id == "zoom-in") Line(9.5f, 6, 9.5f, 13); break;
                case "magnifier":
                    g.DrawEllipse(pen, 3, 3, 13, 13); Line(14, 14, 21, 21); break;
                case "mask":
                    Box(3, 5, 18, 14, 2); Line(6, 16, 16, 6); Line(10, 19, 20, 9); break;
                case "fit-image": Corners(); Box(8, 6, 8, 12, 1); break;
                case "actual": Corners(); Box(8, 8, 8, 8, 1); break;
                case "fit-width":
                    Line(3, 4, 3, 20); Line(21, 4, 21, 20); Line(5, 12, 19, 12);
                    Lines(new(8, 9), new(5, 12), new(8, 15)); Lines(new(16, 9), new(19, 12), new(16, 15)); break;
                case "crop":
                    Lines(new(6, 2), new(6, 18), new(22, 18)); Lines(new(2, 6), new(18, 6), new(18, 22)); break;
                case "settings":
                    Line(3, 6, 21, 6); Line(3, 12, 21, 12); Line(3, 18, 21, 18);
                    Box(7, 4, 3, 4, 1); Box(15, 10, 3, 4, 1); Box(6, 16, 3, 4, 1); break;
                case "language":
                    g.DrawEllipse(pen, 3, 3, 18, 18); g.DrawEllipse(pen, 8, 3, 8, 18); Line(3, 12, 21, 12); break;
                case "startup":
                    Box(4, 4, 16, 16, 2); Lines(new(12, 17), new(12, 7), new(8, 11)); Lines(new(12, 7), new(16, 11)); break;
                case "backup":
                    Box(4, 4, 16, 16, 2); Line(8, 4, 8, 10); Lines(new(12, 8), new(12, 17), new(8, 13)); Lines(new(12, 17), new(16, 13)); break;
                case "log":
                    Box(4, 3, 16, 18, 2); Line(8, 8, 16, 8); Line(8, 12, 18, 12); Line(8, 16, 14, 16); break;
                case "folder":
                    Lines(new(3, 7), new(8, 7), new(10, 5), new(17, 5), new(21, 9), new(21, 19), new(3, 19), new(3, 7)); break;
                case "sound":
                    Lines(new(4, 10), new(8, 10), new(13, 5), new(13, 19), new(8, 14), new(4, 14), new(4, 10));
                    g.DrawArc(pen, 11, 7, 10, 10, -65, 130); break;
                case "notification":
                    g.DrawArc(pen, 5, 4, 14, 15, 180, 180); Line(5, 12, 5, 17); Line(19, 12, 19, 17); Line(5, 17, 19, 17); Line(9, 21, 15, 21); g.DrawEllipse(pen, 11, 2, 2, 2); break;
                case "history":
                    g.DrawArc(pen, 4, 4, 16, 16, 45, 290); Lines(new(4, 5), new(4, 11), new(10, 11)); Line(12, 8, 12, 13); Line(12, 13, 16, 15); break;
                case "window": Box(3, 4, 18, 16, 2); Line(3, 9, 21, 9); Line(8, 4, 8, 9); break;
                case "cursor": Lines(new(5, 3), new(18, 15), new(12, 15), new(9, 21), new(5, 3)); break;
                case "mouse": g.DrawEllipse(pen, 6, 3, 12, 18); Line(12, 3, 12, 11); Line(9, 8, 15, 8); break;
                case "clipboard": Box(6, 4, 12, 17, 2); Box(9, 2, 6, 4, 1); Line(9, 11, 15, 11); Line(9, 15, 15, 15); break;
                case "eye":
                    g.DrawArc(pen, 3, 6, 18, 12, 180, 180); g.DrawArc(pen, 3, 6, 18, 12, 0, 180); g.DrawEllipse(pen, 9, 8, 6, 8); break;
                case "performance":
                    Lines(new(13, 3), new(6, 13), new(12, 13), new(10, 21), new(18, 10), new(12, 10), new(13, 3)); break;
                case "trash":
                    Lines(new(5, 7), new(19, 7)); Line(9, 4, 15, 4); Box(7, 8, 10, 13, 1); Line(10, 11, 10, 18); Line(14, 11, 14, 18); break;
                case "group":
                    g.DrawEllipse(pen, 4, 4, 7, 7); g.DrawEllipse(pen, 13, 4, 7, 7); g.DrawEllipse(pen, 8, 14, 8, 7); Lines(new(7, 11), new(10, 15)); Lines(new(17, 11), new(14, 15)); break;
                case "menu":
                    Line(4, 6, 20, 6); Line(4, 12, 20, 12); Line(4, 18, 20, 18); break;
                case "shadow":
                    Box(4, 3, 13, 13, 1); Lines(new(8, 20), new(20, 20), new(20, 8)); break;
                case "size":
                    Lines(new(4, 8), new(4, 4), new(8, 4)); Lines(new(20, 16), new(20, 20), new(16, 20)); Line(5, 19, 19, 5); break;
                case "tray":
                    Lines(new(3, 8), new(21, 8), new(19, 19), new(5, 19), new(3, 8)); Line(8, 12, 16, 12); Line(12, 9, 12, 15); break;
                case "target":
                    g.DrawEllipse(pen, 4, 4, 16, 16); g.DrawEllipse(pen, 8, 8, 8, 8); Line(12, 2, 12, 6); Line(12, 18, 12, 22); Line(2, 12, 6, 12); Line(18, 12, 22, 12); break;
                case "crosshair":
                    Line(12, 2, 12, 22); Line(2, 12, 22, 12); g.DrawEllipse(pen, 7, 7, 10, 10); break;
                case "font":
                    Line(4, 20, 10, 4); Line(10, 4, 16, 20); Line(7, 14, 13, 14); Line(17, 7, 21, 7); Line(19, 7, 19, 19); Line(16, 19, 22, 19); break;
                case "admin":
                    Lines(new(12, 3), new(20, 7), new(18, 17), new(12, 21), new(6, 17), new(4, 7), new(12, 3)); Line(8, 12, 11, 15); Line(11, 15, 17, 9); break;
                case "image":
                    Box(3, 4, 18, 16, 2); g.DrawEllipse(pen, 7, 8, 3, 3); Lines(new(4, 18), new(10, 12), new(14, 16), new(17, 13), new(21, 18)); break;
                case "opacity":
                    g.DrawEllipse(pen, 4, 4, 16, 16); Lines(new(5, 19), new(19, 5)); break;
                case "gear":
                    Lines(new(9, 3), new(15, 3), new(16, 6), new(19, 6), new(21, 11), new(19, 13), new(19, 17),
                        new(15, 20), new(12, 18), new(9, 20), new(5, 17), new(5, 13), new(3, 11), new(5, 6), new(8, 6), new(9, 3));
                    g.DrawEllipse(pen, 9, 9, 6, 6); break;
                case "exit":
                    Lines(new(11, 4), new(4, 4), new(4, 20), new(11, 20)); Line(9, 12, 21, 12);
                    Lines(new(17, 8), new(21, 12), new(17, 16)); break;
                case "capture-copy":
                    Lines(new(3, 12), new(3, 3), new(12, 3)); Line(16, 3, 20, 3);
                    Line(3, 16, 3, 20); Box(13, 13, 8, 8, 1); Box(8, 8, 8, 8, 1); break;
                case "copy-exit":
                    Box(3, 5, 9, 13, 1); Lines(new(7, 3), new(15, 3), new(15, 8)); Line(13, 13, 22, 13);
                    Lines(new(19, 10), new(22, 13), new(19, 16)); break;
                case "document":
                    Lines(new(5, 3), new(15, 3), new(20, 8), new(20, 21), new(5, 21), new(5, 3));
                    Lines(new(15, 3), new(15, 8), new(20, 8)); Line(8, 13, 16, 13); Line(8, 17, 14, 17); break;
                case "hash":
                    Line(9, 4, 6, 20); Line(18, 4, 15, 20); Line(4, 9, 21, 9); Line(3, 15, 20, 15); break;
                case "anchors":
                    Box(5, 5, 14, 14, 1);
                    Box(3, 3, 4, 4, 1); Box(17, 3, 4, 4, 1); Box(3, 17, 4, 4, 1); Box(17, 17, 4, 4, 1); break;
                case "magnifier-mask":
                    g.DrawEllipse(pen, 3, 3, 13, 13); Line(14, 14, 21, 21); Line(6, 12, 12, 6); Line(10, 14, 14, 10); break;
                case "cursor-off":
                case "cursor-through":
                    Lines(new(5, 3), new(18, 15), new(12, 15), new(9, 21), new(5, 3));
                    if (id == "cursor-off") Line(3, 3, 21, 21);
                    else Lines(new(16, 5), new(21, 5), new(21, 10)); break;
                case "feedback":
                    Lines(new(4, 4), new(20, 4), new(20, 16), new(11, 16), new(6, 20), new(6, 16), new(4, 16), new(4, 4));
                    Line(8, 8, 16, 8); Line(8, 12, 13, 12); break;
                case "key-left":
                case "key-right":
                    Box(3, 5, 18, 14, 2);
                    if (id == "key-left") Lines(new(13, 8), new(9, 12), new(13, 16));
                    else Lines(new(11, 8), new(15, 12), new(11, 16)); break;
                case "mouse-left":
                case "mouse-middle":
                    g.DrawEllipse(pen, 6, 3, 12, 18); Line(12, 3, 12, 8);
                    if (id == "mouse-left") Line(8, 8, 10, 8);
                    else Box(10, 7, 4, 6, 1); break;
                case "enter":
                    Lines(new(20, 6), new(20, 14), new(5, 14)); Lines(new(9, 10), new(5, 14), new(9, 18)); break;
                case "fit-height":
                    Line(4, 3, 20, 3); Line(4, 21, 20, 21); Line(12, 5, 12, 19);
                    Lines(new(9, 8), new(12, 5), new(15, 8)); Lines(new(9, 16), new(12, 19), new(15, 16)); break;
                case "image-reset":
                    Box(3, 4, 14, 14, 2); Lines(new(4, 15), new(8, 10), new(13, 15));
                    g.DrawArc(pen, 13, 12, 8, 8, -90, 235); Lines(new(16, 20), new(13, 19), new(13, 16)); break;
                case "code":
                    Lines(new(7, 7), new(3, 12), new(7, 17)); Lines(new(17, 7), new(21, 12), new(17, 17)); Line(14, 5, 10, 19); break;
                case "notification-save":
                    g.DrawArc(pen, 4, 4, 12, 14, 180, 180); Lines(new(4, 11), new(4, 17), new(12, 17)); Line(8, 21, 12, 21);
                    DownArrow(18, 12); break;
                case "folder-fast":
                    Lines(new(3, 18), new(3, 6), new(9, 6), new(11, 8), new(21, 8), new(21, 18), new(3, 18));
                    Lines(new(12, 10), new(9, 13), new(14, 13), new(11, 16)); break;
                case "tag":
                    Lines(new(3, 3), new(12, 3), new(21, 12), new(12, 21), new(3, 12), new(3, 3)); g.DrawEllipse(pen, 6, 6, 3, 3); break;
                case "app":
                    Box(3, 4, 18, 16, 2); Box(6, 8, 4, 4, 1); Line(13, 10, 18, 10); Line(6, 16, 18, 16); break;
                case "branch":
                    g.DrawEllipse(pen, 4, 3, 5, 5); g.DrawEllipse(pen, 4, 16, 5, 5); g.DrawEllipse(pen, 15, 3, 5, 5);
                    Line(6.5f, 8, 6.5f, 16); Lines(new(17.5f, 8), new(17.5f, 10), new(14, 14), new(7, 14)); break;
                case "license":
                    Lines(new(4, 20), new(4, 4), new(20, 4), new(20, 11)); Line(8, 8, 16, 8); Line(8, 12, 12, 12);
                    g.DrawEllipse(pen, 14, 13, 7, 7); Lines(new(15, 19), new(14, 22), new(17, 21), new(20, 22), new(19, 19)); break;
                case "plugin-install":
                    Box(3, 8, 11, 8, 2); Line(6, 4, 6, 8); Line(11, 4, 11, 8); Line(8, 16, 8, 21); DownArrow(18, 11); break;
                case "group-switch":
                    Box(3, 5, 7, 7, 1); Box(14, 12, 7, 7, 1); Lines(new(13, 5), new(19, 5), new(19, 9));
                    Lines(new(5, 15), new(5, 19), new(11, 19)); break;
                case "close-all":
                    Lines(new(3, 16), new(3, 3), new(16, 3)); Box(8, 8, 13, 13, 2); Line(12, 12, 17, 17); Line(17, 12, 12, 17); break;
                case "keyboard-off":
                    Box(3, 6, 18, 12, 2); Line(6, 10, 18, 10); Line(8, 14, 16, 14); Line(3, 3, 21, 21); break;
                case "pin-history":
                    g.DrawArc(pen, 3, 3, 18, 18, -75, 285); Lines(new(3, 4), new(3, 10), new(9, 10));
                    Lines(new(12, 7), new(18, 13), new(15, 13), new(12, 16), new(9, 13), new(12, 10), new(12, 7));
                    Line(10, 15, 8, 17); break;
                case "plugin": Box(6, 9, 12, 10); Line(9, 4, 9, 9); Line(15, 4, 15, 9); Line(12, 19, 12, 22); break;
                case "palette": g.DrawEllipse(pen, 3, 3, 18, 18); g.DrawEllipse(pen, 7, 7, 2, 2); g.DrawEllipse(pen, 14, 7, 2, 2); g.DrawEllipse(pen, 7, 14, 2, 2); Line(15, 14, 18, 18); break;
                case "output": Lines(new(3, 8), new(3, 5), new(10, 5), new(12, 8), new(21, 8), new(21, 20), new(3, 20), new(3, 8)); Line(10, 14, 17, 14); Lines(new(14, 11), new(17, 14), new(14, 17)); break;
                case "keyboard": Box(2, 5, 20, 14); for(var y=9;y<=12;y+=3) for(var x=6;x<=18;x+=4) Line(x,y,x+.5f,y); Line(7,16,17,16); break;
                case "update": g.DrawArc(pen, 4, 4, 16, 16, 40, 280); Lines(new(21, 5), new(21, 11), new(15, 11)); break;
                case "about": g.DrawEllipse(pen, 3, 3, 18, 18); Line(12, 10, 12, 17); Line(12, 7, 12, 7.2f); break;
                case "check": Lines(new(5, 12), new(10, 17), new(20, 6)); break;
                default: Corners(); break;
            }
        }
        finally { g.Restore(state); }
    }

    // Callers pass the source label before translating it.
    public static string? ForLabel(string label) => label switch
    {
        "首选项…" => "gear",
        "开机启动" => "startup",
        "以管理员身份开机启动" => "admin",
        "自动备份配置文件" => "backup",
        "保持快速响应" or "贴出新图片时闪烁" => "performance",
        "增强版托盘菜单" => "menu",
        "日志级别" => "log",
        "配置文件" or "记住上一次使用的图片扩展名" or "程序文件" => "document",
        "显示语言" => "language",
        "识别引擎" or "文字识别" or "识别文字" => "ocr",
        "翻译服务" or "截图翻译" => "translate",
        "界面字体" or "字体" => "font",
        "主题颜色" or "颜色值" or "锚点描边颜色" or "阴影颜色" or "背景颜色" => "palette",
        "托盘图标" => "tray",
        "隐藏 HEX 颜色值开头的 #" => "hash",
        "Hex 颜色值大写" or "文本颜色" or "文件名" or "复制文本" => "text",
        "显示操作提示" or "当前版本" or "帮助" => "about",
        "选区前自动显示放大镜" or "放大镜中显示边框" => "magnifier",
        "放大镜倍数" or "平滑缩放" or "贴图缩放（滚轮 / +/-）" => "zoom-in",
        "边框宽度" or "最大窗口尺寸" => "size",
        "遮罩颜色" => "mask",
        "遮罩不透明度" or "默认不透明度" or "边框闪烁时使窗口半透明" or "调整透明度（滚轮 / +/-）" or "恢复 100% 不透明度" => "opacity",
        "显示锚点" or "页边距" => "anchors",
        "放大镜中显示遮罩" => "magnifier-mask",
        "放大镜中显示锚点" or "自动检测界面元素" or "使用截屏时的位置" => "target",
        "显示全屏十字线" or "复制坐标（截图中）" => "crosshair",
        "显示快捷键提示" or "在右键菜单显示助记键" or "启用全局快捷键" => "keyboard",
        "自动检测窗口" or "贴出新图片时激活" => "window",
        "隐藏 1×1 提示" or "预览" or "隐藏 / 显示所有贴图" => "eye",
        "捕捉鼠标指针" or "以鼠标指针为中心缩放" => "cursor",
        "完全禁用鼠标指针捕捉" => "cursor-off",
        "其他程序窗口激活时退出截屏" or "退出" => "exit",
        "截图及贴图各自的历史上限" or "截屏历史" => "history",
        "恢复历史贴图" => "pin-history",
        "截图后自动复制" or "截屏：同时复制 Windows Bitmap 格式" or "截屏并自动复制" => "capture-copy",
        "截图后自动复制并退出" => "copy-exit",
        "截图音效文件" => "sound",
        "复制到剪贴板时播放音效" or "忽略其他程序模拟的复制命令" or "贴图：同时复制 Windows Bitmap 格式" => "clipboard",
        "保存截图时播放音效" or "自动保存" or "另存为…" or "快速保存" => "save",
        "禁用鼠标滚轮功能" or "使用鼠标滚轮缩放" => "mouse",
        "按 Esc 时弹出确认对话框" or "问题反馈" => "feedback",
        "截屏时屏蔽左 Win 键" => "key-left",
        "截屏时屏蔽右 Win 键" => "key-right",
        "左键双击" => "mouse-left",
        "中键单击" => "mouse-middle",
        "回车键" => "enter",
        "窗口阴影" => "shadow",
        "快速缩略图宽度" or "图片最大宽度" => "fit-width",
        "快速缩略图高度" => "fit-height",
        "缩略图接受重置操作" or "重置贴图" => "image-reset",
        "切换分组时自动闪烁贴图" or "移动到分组" => "group",
        "拖放操作时保留原始图" or "复制图像" => "copy",
        "文字转图片" or "JPEG 图像质量" or "快速缩略图" => "image",
        "总是忽略 HTML 格式" or "运行环境" => "code",
        "不把文件路径转化成贴图" or "自动保存路径" => "folder",
        "显示操作通知" => "notification",
        "快速保存时显示通知" => "notification-save",
        "快速保存路径" => "folder-fast",
        "关闭贴图" or "关闭" => "close",
        "发布版本" => "tag",
        "检查更新" or "重新启动" => "update",
        "更新状态" => "check",
        "程序" => "app",
        "GitHub 项目" => "branch",
        "开源许可证" => "license",
        "插件安装" => "plugin-install",
        "截屏" => "capture",
        "自定义截屏" or "自定义截屏…" => "crop",
        "贴图" or "贴图 / 恢复贴图" or "贴到屏幕" => "pin",
        "切换到另一贴图分组" => "group-switch",
        "关闭全部贴图" => "close-all",
        "清空截屏历史" => "trash",
        "禁用快捷键" => "keyboard-off",
        "二维码识别" or "识别二维码" => "qr",
        "鼠标穿透开关" => "cursor-through",
        "原始大小" => "fit-image",
        "复制到剪贴板" => "copy",
        _ => null
    };
}
