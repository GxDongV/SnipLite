using SnipLite.Plugin.Abstractions;

namespace SnipLite.Plugins;

internal sealed class PluginManagementForm : Form
{
    public PluginManagementForm(PluginManager manager, IPluginHost host, Func<Keys, string, bool> hotkeyAvailable)
    {
        Text = PluginUi.Text("Plugins", "插件管理", "外掛程式管理");
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(800, 520); MinimumSize = new Size(660, 420); StartPosition = FormStartPosition.CenterScreen;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), WrapContents = true };
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = PluginUi.Text("Enabled", "启用"), FillWeight = 35 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = PluginUi.Text("Plugin", "插件"), ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = PluginUi.Text("Version", "版本"), ReadOnly = true, FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = UiText.T("状态"), ReadOnly = true, FillWeight = 180 });
        foreach (var record in manager.Records)
        {
            var row = grid.Rows[grid.Rows.Add(record.Enabled, record.Name, record.Manifest?.Version ?? "—", Status(record))];
            row.Tag = record;
            row.Cells[0].ReadOnly = record.Manifest is null || record.Error.Length > 0;
        }
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        var changing = false;
        grid.CellValueChanged += (_, e) =>
        {
            if (changing || e.RowIndex < 0 || e.ColumnIndex != 0 || grid.Rows[e.RowIndex].Tag is not PluginRecord record) return;
            try { manager.SetEnabled(record, grid.Rows[e.RowIndex].Cells[0].Value is true); grid.Rows[e.RowIndex].Cells[3].Value = Status(record); }
            catch (Exception ex)
            {
                changing = true; grid.Rows[e.RowIndex].Cells[0].Value = record.Enabled; changing = false;
                MessageBox.Show(this, ex.Message, Text);
            }
        };
        var hint = new Label { Dock = DockStyle.Top, Height = 65, Padding = new Padding(10), Text = PluginUi.Text(
            "Plugins start disabled. Enable switches take effect immediately. Install packages from Preferences > Plugins.",
            "插件默认停用，启停立即生效。请在首选项的插件页导入或下载插件包。") };
        var open = new FluentButton { Text = PluginUi.Text("Open plugin folder", "打开插件目录"), AutoSize = true };
        open.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(PluginManager.UserDirectory); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PluginManager.UserDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };
        footer.Controls.Add(open);
        var commands = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, DisplayMember = "Name" };
        foreach (var command in manager.Commands) commands.Items.Add(new CommandChoice(command.Id, command.Title.Get(host.Language)));
        var key = new HotkeyBox { Width = 160 };
        commands.SelectedIndexChanged += (_, _) => { if (commands.SelectedItem is CommandChoice choice) key.Binding = manager.Hotkey(choice.Id); };
        var save = new FluentButton { Text = PluginUi.Text("Set hotkey", "设置快捷键"), AutoSize = true, Enabled = commands.Items.Count > 0 };
        save.Click += (_, _) =>
        {
            if (commands.SelectedItem is not CommandChoice choice) return;
            if (key.Binding != Keys.None && !hotkeyAvailable(key.Binding, choice.Id))
            { MessageBox.Show(this, PluginUi.Text("This hotkey is already in use.", "该快捷键已被占用。"), Text); return; }
            try { manager.SetHotkey(choice.Id, key.Binding); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };
        footer.Controls.AddRange(new Control[] { commands, key, save });
        if (commands.Items.Count > 0) commands.SelectedIndex = 0;
        Controls.Add(grid); Controls.Add(footer); Controls.Add(hint);
        host.StyleWindow(this);
    }

    internal static string Status(PluginRecord record) => record.Error.Length > 0 ? record.Error : record.Enabled && record.Loaded
        ? UiText.T("已加载") : UiText.T("已停用");
    private sealed record CommandChoice(string Id, string Name);
}
