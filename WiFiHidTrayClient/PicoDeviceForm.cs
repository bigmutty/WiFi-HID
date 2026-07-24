using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

/// <summary>
/// Dialog for managing the Pico's WiFi profiles and mDNS hostname while it's connected via USB.
/// Reads/writes wifi_settings.json at the root of the CIRCUITPY drive; code.py reads this file
/// as its only source of WiFi credentials (see the corresponding comment in code.py). Saving the
/// file causes CircuitPython to auto-reload code.py, applying the new settings immediately.
/// </summary>
internal sealed class PicoDeviceForm : Form
{
    private const string SettingsFileName = "wifi_settings.json";

    private readonly string _driveRoot;
    private readonly TextBox _hostnameBox;
    private readonly DataGridView _grid;

    public PicoDeviceForm(string driveRoot)
    {
        _driveRoot = driveRoot;

        Text = "Pico Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        ClientSize = new Size(480, 400);

        var pathLabel = new Label
        {
            Text = $"Device: {_driveRoot}",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 10),
        };

        var hostnamePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        hostnamePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        hostnamePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _hostnameBox = new TextBox { Dock = DockStyle.Fill };
        hostnamePanel.Controls.Add(new Label { Text = "Hostname:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 3, 3) }, 0, 0);
        hostnamePanel.Controls.Add(_hostnameBox, 1, 0);

        var networksLabel = new Label
        {
            Text = "WiFi networks (tried in order until one connects). At least one is required.",
            AutoSize = true,
            Margin = new Padding(3, 12, 3, 4),
        };

        var addCurrentNetworkButton = new Button
        {
            Text = "Add Current Network",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 8, 3, 4),
        };
        addCurrentNetworkButton.Click += (_, _) => AddCurrentNetwork();

        var networksHeaderPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        networksHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        networksHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        networksHeaderPanel.Controls.Add(networksLabel, 0, 0);
        networksHeaderPanel.Controls.Add(addCurrentNetworkButton, 1, 0);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            Margin = new Padding(3, 3, 3, 3),
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ssid", HeaderText = "SSID" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Password", HeaderText = "Password" });

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        saveButton.Click += (_, _) =>
        {
            if (!TrySave())
            {
                DialogResult = DialogResult.None;
            }
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.Controls.Add(pathLabel, 0, 0);
        outer.Controls.Add(hostnamePanel, 0, 1);
        outer.Controls.Add(networksHeaderPanel, 0, 2);
        outer.Controls.Add(_grid, 0, 3);
        outer.Controls.Add(buttonPanel, 0, 4);

        Controls.Add(outer);

        Load += (_, _) => LoadExistingSettings();
    }

    private void LoadExistingSettings()
    {
        var path = Path.Combine(_driveRoot, SettingsFileName);
        var settings = new PicoWifiSettings();

        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize<PicoWifiSettings>(json) ?? settings;
            }
        }
        catch
        {
            MessageBox.Show(this,
                $"Couldn't read the existing {SettingsFileName}; starting from a blank profile list.",
                "Pico Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _hostnameBox.Text = string.IsNullOrWhiteSpace(settings.Hostname) ? "WiFi-HID" : settings.Hostname;
        foreach (var network in settings.Networks)
        {
            _grid.Rows.Add(network.Ssid, network.Password);
        }
    }

    /// <summary>Looks up the WiFi network this PC is currently connected to and adds it to the
    /// grid (or updates the password if it's already listed), via <see cref="WindowsWifi"/>.</summary>
    private void AddCurrentNetwork()
    {
        if (!WindowsWifi.TryGetCurrentNetwork(out var ssid, out var password, out var error))
        {
            MessageBox.Show(this,
                error ?? "Could not determine the currently connected WiFi network.",
                "Pico Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            if (string.Equals(Convert.ToString(row.Cells["Ssid"].Value), ssid, StringComparison.OrdinalIgnoreCase))
            {
                row.Cells["Password"].Value = password;
                return;
            }
        }

        _grid.Rows.Add(ssid, password);
    }

    private bool TrySave()
    {
        var hostname = _hostnameBox.Text.Trim();
        if (string.IsNullOrEmpty(hostname))
        {
            MessageBox.Show(this, "Hostname cannot be empty.", "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var networks = new List<PicoWifiNetwork>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var ssid = Convert.ToString(row.Cells["Ssid"].Value)?.Trim() ?? string.Empty;
            var password = Convert.ToString(row.Cells["Password"].Value) ?? string.Empty;
            if (string.IsNullOrEmpty(ssid))
            {
                continue;
            }

            networks.Add(new PicoWifiNetwork { Ssid = ssid, Password = password });
        }

        if (networks.Count == 0)
        {
            MessageBox.Show(this, "At least one WiFi network is required.", "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var settings = new PicoWifiSettings { Hostname = hostname, Networks = networks };

        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(_driveRoot, SettingsFileName), json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save to the device: {ex.Message}", "Pico Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        MessageBox.Show(this,
            "Saved. The Pico will automatically reload code.py and reconnect using the new settings.",
            "Pico Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }
}
