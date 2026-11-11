using System.Text.Json.Serialization;

namespace SnipLite;

internal enum CaptureAction { Copy, Pin, QuickSave, None }
internal enum PointerGesture { None, LeftDoubleClick, MiddleClick, RightClick, LeftClick }
internal sealed record MouseBinding(Keys Modifiers, PointerGesture Gesture);
internal sealed record CaptureRegion(int X, int Y, int Width, int Height)
{
    [JsonIgnore] public Rectangle Bounds => new(X, Y, Width, Height);
}

internal sealed partial class AppSettings
{
    public string DisplayLanguage { get; set; } = "zh-CN";
    public bool StartAsAdministrator { get; set; }
    public bool AutoBackup { get; set; } = true;
    public bool KeepResponsive { get; set; } = true;
    public bool EnhancedTrayMenu { get; set; } = true;
    public string LogLevel { get; set; } = "Normal";
    public string InterfaceFont { get; set; } = "Microsoft YaHei UI";
    public float InterfaceFontSize { get; set; } = 9;
    public string TrayIconStyle { get; set; } = "Auto";
    public string CustomTrayIcon { get; set; } = "";
    public bool ColorHex { get; set; }
    public bool ColorHideHash { get; set; }
    public bool ColorUppercase { get; set; } = true;
    public bool MagnifierHints { get; set; } = true;
    public bool AutoMagnifier { get; set; } = true;
    public int CaptureBorderWidth { get; set; } = 1;
    public int CaptureMaskColor { get; set; } = Color.Black.ToArgb();
    public bool CaptureAnchors { get; set; } = true;
    public int CaptureAnchorColor { get; set; } = Color.White.ToArgb();
    public bool MagnifierMask { get; set; } = true;
    public bool MagnifierBorder { get; set; } = true;
    public bool MagnifierAnchors { get; set; }
    public bool CaptureShortcutHints { get; set; } = true;
    public bool DetectWindows { get; set; } = true;
    public bool DetectElements { get; set; } = true;
    public bool HideOnePixelHint { get; set; } = true;
    public bool CaptureCursor { get; set; }
    public bool DisableCursorCapture { get; set; }
    public bool ExitCaptureOnDeactivate { get; set; }
    public int HistoryRegionLimit { get; set; } = 8;
    public bool CycleHistoryRegions { get; set; }
    public int HistoryImageLimit { get; set; } = 20;
    public List<CaptureRegion> RecentRegions { get; set; } = new();
    public string CaptureSound { get; set; } = "";
    public bool SoundOnCopy { get; set; }
    public bool SoundOnSave { get; set; }
    public bool DisableCaptureWheel { get; set; }
    public bool ConfirmEscape { get; set; }
    public bool IgnoreInjectedCopy { get; set; } = true;
    public bool BlockLeftWindows { get; set; }
    public bool BlockRightWindows { get; set; }
    public CaptureAction DoubleClickAction { get; set; } = CaptureAction.Copy;
    public bool DoubleClickExit { get; set; } = true;
    public CaptureAction MiddleClickAction { get; set; } = CaptureAction.Pin;
    public bool MiddleClickExit { get; set; } = true;
    public CaptureAction EnterAction { get; set; } = CaptureAction.Copy;
    public bool EnterExit { get; set; } = true;
    public bool PinShadow { get; set; } = true;
    public int PinShadowColor { get; set; } = Color.Black.ToArgb();
    public bool PinMenuShortcuts { get; set; } = true;
    public int PinMaxSize { get; set; } = 12000;
    public int ThumbnailWidth { get; set; } = 75;
    public int ThumbnailHeight { get; set; } = 75;
    public bool PinAtCapturePosition { get; set; } = true;
    public bool PinWheelZoom { get; set; } = true;
    public bool PinZoomAtCursor { get; set; } = true;
    public bool PinSmoothScaling { get; set; } = true;
    public bool ThumbnailAcceptReset { get; set; } = true;
    public bool PinTranslucentFlash { get; set; } = true;
    public bool FlashOnGroupSwitch { get; set; } = true;
    public bool HidePinsDuringFlash { get; set; }
    public bool KeepOriginalOnDrag { get; set; } = true;
    public bool FlashNewPin { get; set; } = true;
    public bool ActivateNewPin { get; set; } = true;
    public int RecoverablePins { get; set; } = 1;
    public bool TextToImage { get; set; } = true;
    public bool IgnoreHtml { get; set; }
    public bool IgnoreFilePaths { get; set; } = true;
    public int TextImageMargin { get; set; } = 15;
    public int TextImageMaxWidth { get; set; } = 900;
    public string TextImageFont { get; set; } = "Microsoft YaHei UI";
    public float TextImageFontSize { get; set; } = 11;
    public int TextImageBackground { get; set; } = Color.White.ToArgb();
    public int TextImageForeground { get; set; } = Color.Black.ToArgb();
    public bool RememberImageExtension { get; set; } = true;
    public string LastImageExtension { get; set; } = ".png";
    public string QuickSaveDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SnipLite");
    public bool QuickSaveToast { get; set; } = true;
    public bool CaptureBitmapClipboard { get; set; } = true;
    public bool PinBitmapClipboard { get; set; }
    public Keys CaptureHotkey { get; set; } = Keys.F1;
    public Keys CopyCaptureHotkey { get; set; } = Keys.None;
    public Keys CustomCaptureHotkey { get; set; } = Keys.None;
    public Keys PinHotkey { get; set; } = Keys.F3;
    public Keys TogglePinsHotkey { get; set; } = Keys.None;
    public Keys SwitchGroupHotkey { get; set; } = Keys.None;
    public Keys MouseThroughHotkey { get; set; } = Keys.None;
    public Keys OcrHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.O;
    public Keys QrHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.Q;
    public Keys PinZoomModifiers { get; set; } = Keys.None;
    public Keys PinOpacityModifiers { get; set; } = Keys.Control;
    public MouseBinding PinCloseMouse { get; set; } = new(Keys.None, PointerGesture.LeftDoubleClick);
    public MouseBinding PinResetMouse { get; set; } = new(Keys.None, PointerGesture.MiddleClick);
    public MouseBinding PinThumbnailMouse { get; set; } = new(Keys.Shift, PointerGesture.LeftDoubleClick);
    public MouseBinding PinCopyTextMouse { get; set; } = new(Keys.Shift, PointerGesture.RightClick);
    [JsonIgnore] public Dictionary<string, string> HotkeyErrors { get; } = new();

    public Font CreateInterfaceFont() => new(InterfaceFont, InterfaceFontSize);
    public string FormatColor(Color color, bool? hex = null)
    {
        if (!(hex ?? ColorHex)) return $"RGB({color.R}, {color.G}, {color.B})";
        var format = ColorUppercase ? "X2" : "x2";
        return (ColorHideHash ? "" : "#") + color.R.ToString(format) + color.G.ToString(format) + color.B.ToString(format);
    }
    private void NormalizePreferences()
    {
        if (!Enum.IsDefined(OcrEngine)) OcrEngine = OcrEngineKind.SimdPaddle;
        TranslationSourceLanguage = TranslationLanguages.Normalize(TranslationSourceLanguage, allowAuto: true, "en");
        TranslationTargetLanguage = TranslationLanguages.Normalize(TranslationTargetLanguage, allowAuto: false, "zh-CN");
        if (!Enum.IsDefined(TranslationProvider)) TranslationProvider = TranslationProviderKind.Bing;
        InterfaceFontSize = Math.Clamp(InterfaceFontSize, 7, 24);
        TextImageFontSize = Math.Clamp(TextImageFontSize, 6, 100);
        TextImageMargin = Math.Clamp(TextImageMargin, 0, 200);
        TextImageMaxWidth = Math.Clamp(TextImageMaxWidth, 100, 12000);
        CaptureBorderWidth = Math.Clamp(CaptureBorderWidth, 1, 10);
        PinMaxSize = Math.Clamp(PinMaxSize, 100, 20000);
        ThumbnailWidth = Math.Clamp(ThumbnailWidth, 20, 1000);
        ThumbnailHeight = Math.Clamp(ThumbnailHeight, 20, 1000);
        HistoryRegionLimit = Math.Clamp(HistoryRegionLimit, 0, 100);
        HistoryImageLimit = Math.Clamp(HistoryImageLimit, 0, 100);
        RecoverablePins = Math.Clamp(RecoverablePins, 0, 20);
        RecentRegions ??= new();
        RecentRegions = RecentRegions.Where(r => r.Width > 0 && r.Height > 0).Take(HistoryRegionLimit).ToList();
        if (string.IsNullOrWhiteSpace(QuickSaveDirectory)) QuickSaveDirectory = new AppSettings().QuickSaveDirectory;
        if (string.IsNullOrWhiteSpace(InterfaceFont)) InterfaceFont = "Microsoft YaHei UI";
        if (string.IsNullOrWhiteSpace(TextImageFont)) TextImageFont = "Microsoft YaHei UI";
    }
}
