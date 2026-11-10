namespace SnipLite;

internal sealed partial class CaptureForm
{
    private TranslationPanel? _translationPanel;
    private string? _translationInput;
    private CancellationTokenSource? _translationCancellation;
    private int _translationRequest;
    private ITranslationProvider _translationProvider = new GooglePublicTranslationProvider();
    private readonly Dictionary<(string Text, string Source, string Target), TranslationResult> _translationCache = new();
    private bool TranslationVisible => _translationPanel is { Visible: true };

    private void EnsureTranslationPanel()
    {
        if (_translationPanel is not null) return;
        _translationPanel = new TranslationPanel(Ui, _settings.TranslationSourceLanguage, _settings.TranslationTargetLanguage, _settings.Accent)
        {
            Visible = false, Height = Math.Min((int)Math.Round(232 * Ui), Math.Max(160, ClientSize.Height - (int)(60 * Ui)))
        };
        _translationPanel.LanguagesChanged += swapped =>
        {
            if (swapped && !string.IsNullOrWhiteSpace(_translationPanel.ResultText))
            {
                _translationInput = _translationPanel.ResultText;
                _translationPanel.SetOriginal(_translationInput);
            }
            _settings.TranslationSourceLanguage = _translationPanel.SourceLanguage;
            _settings.TranslationTargetLanguage = _translationPanel.TargetLanguage;
            _settings.Save();
            if (!string.IsNullOrWhiteSpace(_translationInput)) _ = TranslateRecognizedTextAsync();
        };
        _translationPanel.RetryRequested += () =>
        {
            if (string.IsNullOrWhiteSpace(_translationInput)) _ = TranslateSelectionAsync();
            else _ = TranslateRecognizedTextAsync(force: true);
        };
        _translationPanel.MenuOpening += menu =>
        {
            _optionMenus.Add(menu);
            menu.Closed += (_, _) => _optionMenus.Remove(menu); // The panel owns and disposes it later.
        };
        Controls.Add(_translationPanel);
    }

    private async Task TranslateSelectionAsync()
    {
        if (!_hasSelection || _recognitionBusy || _ocrMode || _finished) return;
        CommitText(); ClearQrResult();
        var crop = Crop;
        if (crop.Width < 1 || crop.Height < 1) return;
        EnsureTranslationPanel();
        CancelTranslation();
        _translationInput = null;
        _translationPanel!.SetOriginal("");
        _translationPanel.SetBusy("正在识别选区文字…");
        _translationPanel.Visible = true;
        _translationPanel.BringToFront();
        _bar.TranslationMode = true;
        _bar.Status = "";
        _showMagnifier = false;
        _recognitionBusy = true;
        var version = ++_recognitionVersion;
        UpdateCursor(); Invalidate();
        using var image = RenderComposite(crop);
        try
        {
            var document = await Task.Run(() => Services.RecognizeTextAsync(image, _settings.OcrLanguage, _settings.OcrEngine));
            if (IsDisposed || !TranslationVisible || version != _recognitionVersion) return;
            _recognitionBusy = false;
            _translationInput = document.Text;
            _translationPanel.SetOriginal(document.Text);
            if (string.IsNullOrWhiteSpace(document.Text))
                _translationPanel.SetError("没有识别到文字，请按 Esc 返回后调整选区。");
            else await TranslateRecognizedTextAsync();
        }
        catch
        {
            if (!IsDisposed && TranslationVisible && version == _recognitionVersion)
            {
                _recognitionBusy = false;
                _translationPanel.SetError("文字识别失败，请重试或按 Esc 返回后调整选区。");
            }
        }
        finally { if (!IsDisposed) { UpdateCursor(); Invalidate(); } }
    }

    private async Task TranslateRecognizedTextAsync(bool force = false)
    {
        if (!TranslationVisible || string.IsNullOrWhiteSpace(_translationInput)) return;
        _translationCancellation?.Cancel();
        // Each request disposes its own source after its continuation finishes.
        using var cancellation = new CancellationTokenSource();
        _translationCancellation = cancellation;
        var request = ++_translationRequest;
        var key = (Text: _translationInput, Source: _translationPanel!.SourceLanguage, Target: _translationPanel.TargetLanguage);
        _translationPanel.SetBusy("正在翻译…");
        try
        {
            if (force || !_translationCache.TryGetValue(key, out var result))
            {
                result = await _translationProvider.TranslateAsync(key.Text, key.Source, key.Target, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_translationCache.Count >= 12) _translationCache.Clear();
                _translationCache[key] = result;
            }
            if (IsDisposed || !TranslationVisible || request != _translationRequest || cancellation.IsCancellationRequested) return;
            _translationPanel.SetResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsDisposed && TranslationVisible && request == _translationRequest)
                _translationPanel.SetError(ex is InvalidOperationException ? ex.Message : "翻译失败，请检查网络后重试。");
        }
        finally { if (ReferenceEquals(_translationCancellation, cancellation)) _translationCancellation = null; }
    }

    private void LayoutTranslationPanel()
    {
        if (!TranslationVisible) return;
        var bounds = new Rectangle(_bar.MainBounds.Left, _bar.Bounds.Bottom + (int)Math.Round(4 * Ui),
            _bar.MainBounds.Width, _translationPanel!.Height);
        if (_translationPanel.Bounds != bounds) _translationPanel.Bounds = bounds;
    }

    private void CancelTranslation()
    {
        _translationRequest++;
        _translationCancellation?.Cancel();
        _translationCancellation = null;
        _translationPanel?.CloseMenus();
    }

    private void CloseTranslation()
    {
        CancelTranslation();
        _recognitionVersion++;
        _recognitionBusy = false;
        if (_translationPanel is not null) _translationPanel.Visible = false;
        _translationInput = null;
        _translationCache.Clear();
        _bar.TranslationMode = false;
        _bar.AttachedPanelHeight = 0;
        _bar.Status = "";
        Focus(); UpdateCursor(); Invalidate();
    }
}
