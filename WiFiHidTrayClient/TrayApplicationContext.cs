using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace WiFiHidTrayClient;

/// <summary>Tray-only application shell: no main window, just a NotifyIcon and context menu.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly PicoClient _client;
    private readonly InputCapture _capture;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _sendCtrlAltDelItem;
    private readonly ToolStripMenuItem _joystickItem;
    private readonly ToolStripMenuItem _picoSettingsItem;
    private readonly System.Windows.Forms.Timer _picoDetectTimer;
    private string? _picoDriveRoot;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _client = new PicoClient(_settings.Host, _settings.Port);
        _capture = new InputCapture(_client, _settings);

        _statusItem = new ToolStripMenuItem("Disconnected") { Enabled = false };
        _toggleItem = new ToolStripMenuItem($"Start Capturing ({DescribeHotkey()})");
        _toggleItem.Click += (_, _) => _capture.ToggleCapturing();

        var sendCtrlAltDelItem = new ToolStripMenuItem($"Send Ctrl+Alt+Del ({DescribeCtrlAltDelHotkey()})");
        sendCtrlAltDelItem.Click += (_, _) => _capture.SendCtrlAltDelToRemote();
        _sendCtrlAltDelItem = sendCtrlAltDelItem;

        _joystickItem = new ToolStripMenuItem($"Start Joystick Mode ({DescribeJoystickHotkey()})");
        _joystickItem.Click += (_, _) => _capture.ToggleJoystickMode();

        var settingsItem = new ToolStripMenuItem("Open Settings...");
        settingsItem.Click += (_, _) => OpenSettings();

        _picoSettingsItem = new ToolStripMenuItem("Pico Settings...") { Visible = false };
        _picoSettingsItem.Click += (_, _) => OpenPicoSettings();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(sendCtrlAltDelItem);
        menu.Items.Add(_joystickItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(_picoSettingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = TrayIcons.Idle,
            Text = "WiFi-HID Client (disconnected)",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => _capture.ToggleCapturing();

        _client.ConnectionChanged += OnConnectionChanged;
        _capture.CapturingChanged += OnCapturingChanged;
        _capture.JoystickModeChanged += OnJoystickModeChanged;

        _capture.Start();

        // Poll for the Pico's CIRCUITPY drive rather than reacting to WM_DEVICECHANGE, since a
        // few seconds of latency detecting a USB (re)connect is an acceptable trade-off for the
        // much simpler implementation.
        _picoDetectTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _picoDetectTimer.Tick += (_, _) => DetectPicoDevice();
        _picoDetectTimer.Start();
        DetectPicoDevice();
    }

    private string DescribeHotkey()
    {
        var parts = new List<string>();
        if (_settings.ToggleRequiresControl) parts.Add("Ctrl");
        if (_settings.ToggleRequiresAlt) parts.Add("Alt");
        if (_settings.ToggleRequiresShift) parts.Add("Shift");
        parts.Add(_settings.ToggleKey);
        return string.Join("+", parts);
    }

    private string DescribeCtrlAltDelHotkey()
    {
        var parts = new List<string>();
        if (_settings.SendCtrlAltDelRequiresControl) parts.Add("Ctrl");
        if (_settings.SendCtrlAltDelRequiresAlt) parts.Add("Alt");
        if (_settings.SendCtrlAltDelRequiresShift) parts.Add("Shift");
        parts.Add(_settings.SendCtrlAltDelKey);
        return string.Join("+", parts) + ", while capturing";
    }

    private string DescribeJoystickHotkey()
    {
        var parts = new List<string>();
        if (_settings.JoystickRequiresControl) parts.Add("Ctrl");
        if (_settings.JoystickRequiresAlt) parts.Add("Alt");
        if (_settings.JoystickRequiresShift) parts.Add("Shift");
        parts.Add(_settings.JoystickToggleKey);
        return string.Join("+", parts) + ", while capturing";
    }

    private void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            // Don't leave the local keyboard/mouse swallowed with no one to forward input to.
            _capture.StopCapturing();
        }

        RunOnUiThread(() =>
        {
            _statusItem.Text = connected
                ? $"Connected to {_settings.Host}:{_settings.Port}"
                : "Disconnected - retrying...";
            RefreshTrayText();
        });
    }

    private void OnCapturingChanged(bool capturing)
    {
        RunOnUiThread(() =>
        {
            _toggleItem.Text = capturing
                ? $"Stop Capturing ({DescribeHotkey()})"
                : $"Start Capturing ({DescribeHotkey()})";
            _trayIcon.Icon = capturing ? TrayIcons.Capturing : TrayIcons.Idle;
            RefreshTrayText();
            _trayIcon.ShowBalloonTip(1500, "WiFi-HID Client",
                capturing
                    ? $"Capturing keyboard & mouse. Press {DescribeHotkey()} to release."
                    : "Input capturing released - local keyboard/mouse restored.",
                ToolTipIcon.Info);
        });
    }

    private void RefreshTrayText()
    {
        var connState = _client.IsConnected ? "connected" : "disconnected";
        var capState = _capture.IsCapturing ? "CAPTURING" : "idle";
        var text = _capture.IsJoystickMode
            ? $"WiFi-HID ({connState}, {capState}, JOYSTICK)"
            : $"WiFi-HID ({connState}, {capState})";
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void OnJoystickModeChanged(bool joystickMode)
    {
        RunOnUiThread(() =>
        {
            _joystickItem.Text = joystickMode
                ? $"Stop Joystick Mode ({DescribeJoystickHotkey()})"
                : $"Start Joystick Mode ({DescribeJoystickHotkey()})";
            RefreshTrayText();
            _trayIcon.ShowBalloonTip(1500, "WiFi-HID Client",
                joystickMode
                    ? $"Joystick Mode on - mouse movement now simulates a joystick. Press {DescribeJoystickHotkey()} to release."
                    : "Joystick Mode off - mouse movement restored to normal.",
                ToolTipIcon.Info);
        });
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings.Host = form.Host;
        _settings.Port = form.Port;
        _settings.ToggleRequiresControl = form.ToggleRequiresControl;
        _settings.ToggleRequiresAlt = form.ToggleRequiresAlt;
        _settings.ToggleRequiresShift = form.ToggleRequiresShift;
        _settings.ToggleKey = form.ToggleKey;
        _settings.SendCtrlAltDelRequiresControl = form.SendCtrlAltDelRequiresControl;
        _settings.SendCtrlAltDelRequiresAlt = form.SendCtrlAltDelRequiresAlt;
        _settings.SendCtrlAltDelRequiresShift = form.SendCtrlAltDelRequiresShift;
        _settings.SendCtrlAltDelKey = form.SendCtrlAltDelKey;
        _settings.JoystickRequiresControl = form.JoystickRequiresControl;
        _settings.JoystickRequiresAlt = form.JoystickRequiresAlt;
        _settings.JoystickRequiresShift = form.JoystickRequiresShift;
        _settings.JoystickToggleKey = form.JoystickToggleKey;
        _settings.Save();

        _client.UpdateEndpoint(_settings.Host, _settings.Port);
        _capture.RefreshHotkeys();

        _toggleItem.Text = _capture.IsCapturing
            ? $"Stop Capturing ({DescribeHotkey()})"
            : $"Start Capturing ({DescribeHotkey()})";
        _sendCtrlAltDelItem.Text = $"Send Ctrl+Alt+Del ({DescribeCtrlAltDelHotkey()})";
        _joystickItem.Text = _capture.IsJoystickMode
            ? $"Stop Joystick Mode ({DescribeJoystickHotkey()})"
            : $"Start Joystick Mode ({DescribeJoystickHotkey()})";
        RefreshTrayText();
    }

    private void DetectPicoDevice()
    {
        bool found = PicoUsbDevice.TryFindCircuitPyDrive(out var root);
        _picoDriveRoot = found ? root : null;
        _picoSettingsItem.Visible = found;
    }

    private void OpenPicoSettings()
    {
        if (_picoDriveRoot == null || !System.IO.Directory.Exists(_picoDriveRoot))
        {
            MessageBox.Show("The Pico's CIRCUITPY drive is no longer available. Reconnect the device via USB and try again.",
                "Pico Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new PicoDeviceForm(_picoDriveRoot);
        form.ShowDialog();
    }

    private void ExitApp()
    {
        _picoDetectTimer.Stop();
        _picoDetectTimer.Dispose();
        _trayIcon.Visible = false;
        _capture.Dispose();
        _client.Dispose();
        Application.Exit();
    }

    private void RunOnUiThread(Action action)
    {
        var target = _trayIcon.ContextMenuStrip;
        if (target != null && target.InvokeRequired)
        {
            target.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
