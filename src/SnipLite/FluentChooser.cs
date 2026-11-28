namespace SnipLite;

internal static class FluentChooser
{
    internal static Button Create(IReadOnlyList<string> options, int selected, Action<int> onChange, bool nativeLanguages = false)
    {
        var button = new FluentButton
        {
            Tag = nativeLanguages ? "localization:native" : null,
            DropDown = true,
            Text = options[Math.Clamp(selected, 0, options.Count - 1)],
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Current.Field,
            ForeColor = AppTheme.Current.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            TabStop = true
        };

        button.FlatAppearance.BorderColor = AppTheme.Current.Border;
        button.FlatAppearance.MouseOverBackColor = AppTheme.Current.Hover;
        button.FlatAppearance.MouseDownBackColor = AppTheme.Current.AccentSoft;

        // Keep one menu per chooser. ContextMenuStrip is a shared-reference
        // property, so the button does not dispose it automatically. Release
        // it after the closing stack unwinds when its page is rebuilt.
        selected = Math.Clamp(selected, 0, options.Count - 1);
        var menu = SnipMenu.Create(roundedPopup: true);
        var items = new ToolStripMenuItem[options.Count];

        button.ContextMenuStrip = menu;
        button.Disposed += (_, _) => SnipMenu.DisposeAfterClose(menu);

        for (var i = 0; i < options.Count; i++)
        {
            var index = i;

            items[i] = SnipMenu.Item(options[i], () =>
            {
                if (!button.Enabled) return;
                selected = index;
                button.Text = options[index];
                onChange(index);
            });
            items[i].Tag = null;
            if (nativeLanguages) items[i].Tag = "localization:native";
            items[i].Text = options[i];

            menu.Items.Add(items[i]);
        }

        menu.Opening += (_, e) =>
        {
            if (!button.Enabled) { e.Cancel = true; return; }
            menu.MinimumSize = new Size(button.Width, 0);
            var width = Math.Max(button.Width, menu.GetPreferredSize(Size.Empty).Width);
            for (var i = 0; i < items.Length; i++)
            {
                items[i].Checked = i == selected;
                // The popup minimum width does not stretch WinForms' menu items.
                var preferred = items[i].GetPreferredSize(Size.Empty);
                items[i].AutoSize = false;
                items[i].Size = new Size(width, preferred.Height);
            }
        };

        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height + Math.Max(2, button.DeviceDpi / 48)));
        button.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Down or Keys.Up or Keys.F4)) return;
            menu.Show(button, new Point(0, button.Height + Math.Max(2, button.DeviceDpi / 48)));
            items[Math.Clamp(selected, 0, items.Length - 1)].Select();
            e.Handled = e.SuppressKeyPress = true;
        };
        return button;
    }
}
