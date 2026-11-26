using SnipLite.Plugins;

namespace SnipLite;

internal sealed partial class PreferencesForm
{
    private const int ToolbarPreviewH = 104;
    private bool IsLayoutPage => _selected == 0 && _subPages[0] == 1;

    private void LayoutPage()
    {
        var commands = _plugins?.Commands ?? [];
        var buttons = commands.Select(command => (command.Id, command.Title.Get(Localization.LanguageService.CurrentLanguage), command.Icon)).ToArray();
        var title = new Label
        {
            Text = "截图工具栏预览",
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(_labelX, _y, ContentWidth(), Px(CaptionH)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var preview = new ToolbarLayoutPreview(_s, buttons)
        {
            Bounds = new Rectangle(_labelX, _y + title.Height, ContentWidth(), Px(ToolbarPreviewH)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _layoutHeader.Controls.Add(title);
        _layoutHeader.Controls.Add(preview);
        Value(nameof(_s.ToolbarButtonOrder));
        preview.OrderChanged += order => Set(nameof(_s.ToolbarButtonOrder), order);

        _y = Px(PagePad);
        _captions = true;
        Section("截图功能按钮", () => { });
        HintText("拖动上方预览中的按钮调整顺序。下方开关控制按钮显示，快捷键照常可用。");
        Value(nameof(_s.HiddenToolbarButtons));
        foreach (var (id, label) in AnnotationBar.BuiltInButtons)
            ToolbarSwitch(preview, id, label);
        Section("插件功能按钮", () => { });
        if (buttons.Length == 0) HintText("启用插件后，可在这里控制插件按钮的显示。");
        foreach (var (id, label, icon) in buttons)
            ToolbarSwitch(preview, id, label, icon);
    }

    private void ToolbarSwitch(ToolbarLayoutPreview preview, string id, string title, Image? pluginIcon = null) =>
        Check(title, !_s.HiddenToolbarButtons.Contains(id), visible =>
        {
            Change(() =>
            {
                // Keep the defaults object and the active settings in separate collections.
                var hidden = new HashSet<string>(_s.HiddenToolbarButtons, StringComparer.Ordinal);
                if (visible) hidden.Remove(id); else hidden.Add(id);
                _s.HiddenToolbarButtons = hidden;
            });
            preview.RefreshToolbar();
        }, FluentIcons.Supports(id) ? id : "plugin", pluginIcon);

    private void PluginsPage()
    {
        if (_plugins is not { } plugins) { HintText("插件管理不可用。"); return; }
        HintText("导入或下载插件包后手动启用。插件开关控制功能，常规 → 布局控制工具栏按钮。");
        var import = new FluentButton(); StyleButton(import, "导入插件…");
        import.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "SnipLite plugin (*.snipplugin;*.zip)|*.snipplugin;*.zip", CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { plugins.ImportPackage(dialog.FileName); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var download = new FluentButton(); StyleButton(download, "下载插件…");
        download.Click += (_, _) => DownloadPlugin(plugins);
        var open = new FluentButton(); StyleButton(open, "打开插件目录");
        open.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(PluginManager.UserDirectory); Open(PluginManager.UserDirectory, false); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };
        Row("插件安装", import, download, open);
        if (plugins.HasPendingChanges)
        {
            HintText("插件更新或卸载将在重启后生效。");
            if (PluginRestartRequested is { } restart)
            {
                var apply = new FluentButton(); StyleButton(apply, "立即重启");
                apply.Click += (_, _) =>
                {
                    try { restart(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };
                Row("", apply);
            }
        }
        if (plugins.Records.Count == 0) HintText("未安装插件。请导入插件包或从下载地址安装。");
        foreach (var record in plugins.Records)
        {
            // One plugin, one row: name, version, status and the enable switch.
            var row = NewSettingRow(Px(64));
            AddRowIcon(row, record.Name, "plugin", record.Icon);
            var version = record.Manifest is null ? "" : " v" + record.Manifest.Version;
            if (record.PendingVersion is { } pending) version += " → v" + pending;
            var actionWidth = 0;
            void AddAction(string label, string prompt, bool enabled, Action apply)
            {
                var button = new FluentButton { Enabled = enabled };
                StyleButton(button, label);
                button.Bounds = new Rectangle(row.Width - button.Width - Px(14) - actionWidth,
                    (row.Height - button.Height) / 2, button.Width, button.Height);
                button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                actionWidth += button.Width + Px(12);
                button.Click += (_, _) =>
                {
                    var message = Localization.LanguageService.Format(prompt, record.Name);
                    if (MessageBox.Show(this, message, button.Text, MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                    try { apply(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };
                row.Controls.Add(button);
            }

            if (record.Manifest is not null)
                AddAction("卸载插件", "重启后将卸载 {0}。保留设置和数据。",
                    plugins.CanUninstall(record), () => plugins.Uninstall(record));
            if (plugins.CanRestoreBundled(record))
                AddAction("恢复内置版本", "重启后将恢复 {0} 的内置版本。保留设置和数据。",
                    true, () => plugins.RestoreBundled(record));
            var toggle = new FluentToggle
            {
                Text = record.Name + version + "  ·  " + record.Status,
                Checked = record.Enabled,
                Enabled = record.Manifest is not null && record.Error.Length == 0,
                Bounds = new Rectangle(Px(48), Px(8), row.Width - Px(62) - actionWidth, row.Height - Px(16)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Tag = "localization:native"
            };
            toggle.CheckedChanged += (_, _) =>
            {
                try { plugins.SetEnabled(record, toggle.Checked); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RefreshPlugins();
                }
            };
            row.Controls.Add(toggle); _y += row.Height + Px(6);
        }
    }

    private IReadOnlyList<CatalogPlugin>? _catalog;

    private void PluginCatalogPage()
    {
        if (_plugins is not { } plugins) { HintText("插件管理不可用。"); return; }

        var refresh = new FluentButton();
        StyleButton(refresh, "刷新插件目录");
        Row("插件目录", refresh);
        var status = new Label
        {
            AutoEllipsis = true,
            Bounds = new Rectangle(_labelX, _y, ContentWidth(), Px(26)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _page.Controls.Add(status);
        _y += status.Height + Px(4);
        var downloadButtons = new List<FluentButton>();
        var cancellation = new CancellationTokenSource();
        refresh.Disposed += (_, _) => { cancellation.Cancel(); cancellation.Dispose(); };
        var token = cancellation.Token;

        async Task RefreshAsync()
        {
            refresh.Enabled = false;
            foreach (var button in downloadButtons) button.Enabled = false;
            status.Text = UiText.T("正在加载插件目录…");
            try
            {
                var entries = await PluginCatalog.FetchAsync(token);
                if (refresh.IsDisposed) return;
                _catalog = entries;
                ShowTab(PluginsTab);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                AppLog.Write("Cannot fetch the plugin catalog.", ex);
                if (!refresh.IsDisposed) status.Text = UiText.T("无法加载插件目录，请重试。");
            }
            finally
            {
                if (!refresh.IsDisposed)
                {
                    refresh.Enabled = true;
                    foreach (var button in downloadButtons) button.Enabled = true;
                }
            }
        }

        refresh.Click += async (_, _) => await RefreshAsync();
        if (_catalog is null)
        {
            BeginInvoke(async () => { if (!refresh.IsDisposed) await RefreshAsync(); });
            return;
        }

        if (_catalog.Count == 0) status.Text = UiText.T("插件目录暂无插件。");
        else
        {
            foreach (var entry in _catalog) AddCatalogRow(plugins, entry, downloadButtons);
        }
    }

    private void AddCatalogRow(PluginManager plugins, CatalogPlugin entry, List<FluentButton> downloadButtons)
    {
        var record = plugins.CatalogRecord(entry.Id);
        var installed = record?.Manifest?.Version;
        var row = NewSettingRow(Px(74));
        AddRowIcon(row, entry.DisplayName, "plugin", record?.Icon);
        var action = new FluentButton();
        var label = !entry.IsCompatible ? "与当前版本不兼容"
            : record?.RestartPending == true ? "重启后生效"
            : installed is null ? "安装插件"
            : PluginManager.CompareVersions(entry.Version, installed) > 0 ? "更新插件" : "已是最新版本";
        StyleButton(action, label);
        action.Bounds = new Rectangle(row.Width - action.Width - Px(14), (row.Height - action.Height) / 2, action.Width, action.Height);
        action.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        action.Enabled = label is "安装插件" or "更新插件";
        if (action.Enabled) downloadButtons.Add(action);
        action.Click += (_, _) =>
        {
            if (DownloadPlugin(plugins, entry)) SelectSubPage(PluginsTab, 0);
        };
        var textWidth = Math.Max(1, action.Left - Px(60));
        var version = label == "更新插件" ? $"v{installed} → v{entry.Version}" : $"v{installed ?? entry.Version}";
        var name = new Label
        {
            Text = $"{entry.DisplayName}  ·  {entry.Author}  ·  {version}",
            AutoEllipsis = true, Tag = "localization:native",
            Bounds = new Rectangle(Px(48), Px(11), textWidth, Px(23)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var description = new Label
        {
            Text = entry.DisplayDescription,
            AutoEllipsis = true, Tag = "localization:native",
            Bounds = new Rectangle(Px(48), Px(36), textWidth, Px(30)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        row.Controls.AddRange([action, name, description]);
        _y += row.Height + Px(6);
    }

    private bool DownloadPlugin(PluginManager plugins, CatalogPlugin? entry = null)
    {
        using var dialog = new Form
        {
            Text = UiText.T("下载插件…"), ClientSize = new Size(620, 205), MinimumSize = new Size(520, 240),
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Font
        };
        var label = new Label { Text = UiText.T("输入插件包的 HTTPS 直链地址（ZIP / snipplugin）。"), AutoSize = false, Bounds = new Rectangle(14, 14, 590, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var address = new TextBox { Bounds = new Rectangle(14, 48, 590, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var status = new Label { Bounds = new Rectangle(14, 86, 590, 42), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var download = new FluentButton { Text = UiText.T("下载安装"), Bounds = new Rectangle(370, 150, 110, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var cancel = new FluentButton { Text = UiText.T("取消"), Bounds = new Rectangle(490, 150, 110, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        if (entry is not null)
        {
            label.Text = $"{entry.DisplayName}  ·  {entry.Author}  ·  v{entry.Version}";
            address.Text = entry.DownloadUrl.AbsoluteUri;
            address.ReadOnly = true;
        }
        using var cancellation = new CancellationTokenSource();
        var busy = false; var closeRequested = false;
        cancel.Click += (_, _) => { closeRequested = true; cancellation.Cancel(); if (!busy) dialog.Close(); };
        dialog.FormClosing += (_, e) => { if (busy) { e.Cancel = true; closeRequested = true; cancellation.Cancel(); } };
        download.Click += async (_, _) =>
        {
            if (!Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https")
            { status.Text = UiText.T("请输入 HTTPS 插件包下载地址。"); return; }
            busy = true; download.Enabled = address.Enabled = false; status.Text = UiText.T("正在下载并验证插件包…");
            var installed = false;
            try
            {
                if (entry is null) await plugins.DownloadPackageAsync(uri, cancellation.Token);
                else await plugins.InstallFromCatalogAsync(entry, cancellation.Token);
                installed = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { status.Text = ex.GetBaseException().Message; }
            finally
            {
                busy = false;
                if (installed) { dialog.DialogResult = DialogResult.OK; dialog.Close(); }
                else if (closeRequested) dialog.Close();
                else download.Enabled = address.Enabled = true;
            }
        };
        dialog.Controls.AddRange(new Control[] { label, address, status, download, cancel });
        AppTheme.Apply(dialog); AppTheme.ApplyWindow(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
}
