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

        _y = Px(PagePad);
        _captions = true;
        Section("截图功能按钮", () => { });
        HintText("分别控制 F1 框选后工具栏中的按钮；隐藏按钮不影响快捷键。");
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
            if (plugins.CanUninstall(record))
            {
                var remove = new FluentButton();
                StyleButton(remove, record.IsScrollCapture ? "恢复内置版本" : "卸载插件");
                remove.Bounds = new Rectangle(row.Width - remove.Width - Px(14), (row.Height - remove.Height) / 2, remove.Width, remove.Height);
                remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                actionWidth = remove.Width + Px(12);
                remove.Click += (_, _) =>
                {
                    var message = Localization.LanguageService.Format(record.IsScrollCapture
                        ? "重启后将恢复 {0} 的内置版本。保留设置和数据。"
                        : "重启后将卸载 {0}。保留设置和数据。", record.Name);
                    if (MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    try { plugins.Uninstall(record); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };
                row.Controls.Add(remove);
            }
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

    private void DownloadPlugin(PluginManager plugins)
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
                await plugins.DownloadPackageAsync(uri, cancellation.Token);
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
        dialog.ShowDialog(this);
    }
}
