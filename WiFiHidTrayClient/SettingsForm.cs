using System;
using System.Drawing;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

/// <summary>
/// Editable settings dialog for the Pico host/port and the two capture hotkeys, replacing the
/// old "edit settings.json by hand" flow. Values are validated (host non-empty, key names must
/// parse as a valid System.Windows.Forms.Keys) before the dialog is allowed to close with OK.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TextBox _hostBox;
    private readonly NumericUpDown _portBox;
    private readonly CheckBox _toggleCtrlBox;
    private readonly CheckBox _toggleAltBox;
    private readonly CheckBox _toggleShiftBox;
    private readonly TextBox _toggleKeyBox;
    private readonly CheckBox _cadCtrlBox;
    private readonly CheckBox _cadAltBox;
    private readonly CheckBox _cadShiftBox;
    private readonly TextBox _cadKeyBox;

    public string Host => _hostBox.Text.Trim();
    public int Port => (int)_portBox.Value;
    public bool ToggleRequiresControl => _toggleCtrlBox.Checked;
    public bool ToggleRequiresAlt => _toggleAltBox.Checked;
    public bool ToggleRequiresShift => _toggleShiftBox.Checked;
    public string ToggleKey => _toggleKeyBox.Text.Trim();
    public bool SendCtrlAltDelRequiresControl => _cadCtrlBox.Checked;
    public bool SendCtrlAltDelRequiresAlt => _cadAltBox.Checked;
    public bool SendCtrlAltDelRequiresShift => _cadShiftBox.Checked;
    public string SendCtrlAltDelKey => _cadKeyBox.Text.Trim();

    public SettingsForm(AppSettings settings)
    {
        Text = "WiFi-HID Client Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(400, 350);
        AutoSize = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _hostBox = new TextBox { Text = settings.Host, Dock = DockStyle.Fill };
        _portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(settings.Port, 1, 65535), Dock = DockStyle.Fill };

        _toggleKeyBox = new TextBox { Text = settings.ToggleKey, Dock = DockStyle.Fill };
        _toggleCtrlBox = new CheckBox { Text = "Ctrl", Checked = settings.ToggleRequiresControl, AutoSize = true };
        _toggleAltBox = new CheckBox { Text = "Alt", Checked = settings.ToggleRequiresAlt, AutoSize = true };
        _toggleShiftBox = new CheckBox { Text = "Shift", Checked = settings.ToggleRequiresShift, AutoSize = true };

        _cadKeyBox = new TextBox { Text = settings.SendCtrlAltDelKey, Dock = DockStyle.Fill };
        _cadCtrlBox = new CheckBox { Text = "Ctrl", Checked = settings.SendCtrlAltDelRequiresControl, AutoSize = true };
        _cadAltBox = new CheckBox { Text = "Alt", Checked = settings.SendCtrlAltDelRequiresAlt, AutoSize = true };
        _cadShiftBox = new CheckBox { Text = "Shift", Checked = settings.SendCtrlAltDelRequiresShift, AutoSize = true };

        int row = 0;
        AddRow(layout, ref row, "Pico host / IP:", _hostBox);
        AddRow(layout, ref row, "Pico port:", _portBox);
        AddSectionLabel(layout, ref row, "Capture toggle hotkey");
        AddRow(layout, ref row, "Modifiers:", FlowOf(_toggleCtrlBox, _toggleAltBox, _toggleShiftBox));
        AddRow(layout, ref row, "Key:", _toggleKeyBox);
        AddSectionLabel(layout, ref row, "Send Ctrl+Alt+Del hotkey (while capturing)");
        AddRow(layout, ref row, "Modifiers:", FlowOf(_cadCtrlBox, _cadAltBox, _cadShiftBox));
        AddRow(layout, ref row, "Key:", _cadKeyBox);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(12, 0, 12, 12),
        };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        okButton.Click += (_, _) =>
        {
            if (!HasValidInput())
            {
                DialogResult = DialogResult.None;
            }
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        // FlowLayoutPanel is added first so, with both docked Bottom, it ends up above the grid.
        Controls.Add(buttonPanel);
        Controls.Add(layout);
    }

    private bool HasValidInput()
    {
        if (string.IsNullOrWhiteSpace(_hostBox.Text))
        {
            MessageBox.Show(this, "Host cannot be empty.", "Invalid settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!TryParseKey(_toggleKeyBox.Text, out _))
        {
            MessageBox.Show(this, $"'{_toggleKeyBox.Text}' is not a recognized key name (e.g. F12, Delete, Escape).",
                "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!TryParseKey(_cadKeyBox.Text, out _))
        {
            MessageBox.Show(this, $"'{_cadKeyBox.Text}' is not a recognized key name (e.g. F12, Delete, Escape).",
                "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static bool TryParseKey(string text, out Keys key)
    {
        return Enum.TryParse(text.Trim(), ignoreCase: true, out key) && key != Keys.None;
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string label, Control control)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 3);
        layout.Controls.Add(control, 1, row);
        row++;
    }

    private static void AddSectionLabel(TableLayoutPanel layout, ref int row, string text)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var baseFont = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(baseFont, FontStyle.Bold),
            Margin = new Padding(3, 14, 3, 3),
        };
        layout.Controls.Add(label, 0, row);
        layout.SetColumnSpan(label, 2);
        row++;
    }

    private void InitializeComponent()
    {

    }

    private static FlowLayoutPanel FlowOf(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        foreach (var c in controls)
        {
            c.Margin = new Padding(0, 3, 12, 3);
            flow.Controls.Add(c);
        }

        return flow;
    }
}
