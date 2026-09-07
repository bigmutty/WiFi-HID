using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace WiFiHidTrayClient;

/// <summary>
/// Maintains a persistent TCP connection to the Pico WiFi-HID socket server (see code.py,
/// which reads newline-delimited JSON command objects) and reconnects automatically.
/// All sends happen on a dedicated background thread so that callers on the low-level hook
/// thread never block on network I/O (hooks must return quickly or Windows will silently
/// remove them).
/// </summary>
public sealed class PicoClient : IDisposable
{
    private readonly object _endpointLock = new();
    private string _host;
    private int _port;
    private readonly BlockingCollection<string> _outbox = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _senderThread;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private volatile bool _connected;
    private volatile bool _endpointChanged;

    // Only ever read/written from the sender thread (same thread that calls TryEnsureConnected),
    // so no locking is needed here.
    private string? _resolvedIp;
    private string? _resolvedForHost;

    public event Action<bool>? ConnectionChanged;
    public bool IsConnected => _connected;

    public PicoClient(string host, int port)
    {
        _host = host;
        _port = port;
        _senderThread = new Thread(SenderLoop) { IsBackground = true, Name = "PicoClientSender" };
        _senderThread.Start();
    }

    /// <summary>Switches to a new Pico endpoint, forcing a reconnect on the next send. The
    /// actual socket teardown happens on the sender thread to avoid racing with it.</summary>
    public void UpdateEndpoint(string host, int port)
    {
        lock (_endpointLock)
        {
            _host = host;
            _port = port;
        }

        _endpointChanged = true;
        Enqueue(new { command = "releaseAll" }); // nudges the sender thread to reconnect immediately
    }

    public void SendKeyAction(string key, string action)
        => Enqueue(new { command = "key", key, action });

    public void SendMouseMove(int x, int y)
        => Enqueue(new { command = "mouse", x, y });

    public void SendMouseButton(string button, string action)
        => Enqueue(new { command = "mouse", x = 0, y = 0, action, button });

    public void SendMouseWheel(int wheel)
        => Enqueue(new { command = "mouse", x = 0, y = 0, wheel });

    /// <summary>Sends the full simulated joystick state (see code.py's Joystick.report); the
    /// Pico applies it as-is rather than tracking incremental state.</summary>
    public void SendJoystick(int x, int y, bool button1, bool button2)
        => Enqueue(new { command = "joystick", x, y, button1, button2 });

    /// <summary>
    /// Sends a virtual Ctrl+Alt+Delete to the Pico (Control down, Alt down, Delete press,
    /// Alt up, Control up) - used because the real Ctrl+Alt+Del is intercepted by Winlogon
    /// and can never reach a hook to be forwarded.
    /// </summary>
    public void SendCtrlAltDelete()
    {
        Enqueue(new { command = "key", key = "CONTROL", action = "keyDown" });
        Enqueue(new { command = "key", key = "ALT", action = "keyDown" });
        Enqueue(new { command = "key", key = "DELETE", action = "press" });
        Enqueue(new { command = "key", key = "ALT", action = "keyUp" });
        Enqueue(new { command = "key", key = "CONTROL", action = "keyUp" });
    }

    /// <summary>Clears any keys/buttons the Pico might think are still held, e.g. after a capture toggle.</summary>
    public void SendReleaseAllKeysAndButtons()
    {
        Enqueue(new { command = "releaseAll" });
        Enqueue(new { command = "mouse", x = 0, y = 0, action = "buttonUp", button = "LEFT" });
        Enqueue(new { command = "mouse", x = 0, y = 0, action = "buttonUp", button = "RIGHT" });
        Enqueue(new { command = "mouse", x = 0, y = 0, action = "buttonUp", button = "MIDDLE" });
    }

    private void Enqueue(object payload)
    {
        if (_outbox.IsAddingCompleted)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        try
        {
            _outbox.Add(json);
        }
        catch (InvalidOperationException)
        {
            // Collection was completed concurrently (shutting down); safe to ignore.
        }
    }

    // How long the sender thread waits for an outgoing message before treating the wait as an
    // opportunity to check on / heartbeat the connection instead. This is what lets a dropped
    // Pico connection be detected promptly even while no keyboard/mouse input is happening.
    private const int HeartbeatIntervalMs = 3000;

    private void SenderLoop()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            string? json;
            bool hasItem;
            try
            {
                hasItem = _outbox.TryTake(out json, HeartbeatIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!hasItem)
            {
                // Idle - use this wakeup to make sure the connection is actually still alive
                // rather than waiting for the next real command to discover it's gone.
                if (TryEnsureConnected())
                {
                    SendRaw("{\"command\":\"ping\"}");
                }
                continue;
            }

            if (!TryEnsureConnected())
            {
                // Drop the message; SendReleaseAllKeysAndButtons() is called after every
                // capture toggle so state re-syncs once the connection comes back.
                continue;
            }

            SendRaw(json!);
        }
    }

    private void SendRaw(string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            _stream!.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            DisposeSocket();
        }
    }

    private bool TryEnsureConnected()
    {
        if (_endpointChanged)
        {
            _endpointChanged = false;
            DisposeSocket();
        }

        if (_connected && _tcpClient is { Connected: true })
        {
            return true;
        }

        string host;
        int port;
        lock (_endpointLock)
        {
            host = _host;
            port = _port;
        }

        var connectHost = ResolveConnectHost(host);

        try
        {
            DisposeSocket();
            _tcpClient = new TcpClient();
            if (!_tcpClient.ConnectAsync(connectHost, port).Wait(1000, _cts.Token))
            {
                DisposeSocket();
                InvalidateResolvedIp(host);
                return false;
            }

            _stream = _tcpClient.GetStream();
            SetConnected(true);
            return true;
        }
        catch
        {
            DisposeSocket();
            InvalidateResolvedIp(host);
            return false;
        }
    }

    /// <summary>
    /// If <paramref name="host"/> is already an IP address, returns it as-is. Otherwise (e.g. a
    /// "*.local" mDNS hostname) pings it once to discover its current IP and caches the result,
    /// so subsequent reconnects skip the (often slow) hostname resolution and connect straight
    /// to the IP. Falls back to the hostname itself if the ping fails.
    /// </summary>
    private string ResolveConnectHost(string host)
    {
        if (IPAddress.TryParse(host, out _))
        {
            return host;
        }

        if (_resolvedIp != null && string.Equals(_resolvedForHost, host, StringComparison.OrdinalIgnoreCase))
        {
            return _resolvedIp;
        }

        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, 1000);
            if (reply?.Status == IPStatus.Success && reply.Address != null)
            {
                _resolvedIp = reply.Address.ToString();
                _resolvedForHost = host;
                return _resolvedIp;
            }
        }
        catch
        {
            // Fall back to connecting with the hostname directly below.
        }

        return host;
    }

    /// <summary>Forces the next connection attempt to re-resolve <paramref name="host"/> via ping,
    /// e.g. because a connect using the previously cached IP just failed.</summary>
    private void InvalidateResolvedIp(string host)
    {
        if (string.Equals(_resolvedForHost, host, StringComparison.OrdinalIgnoreCase))
        {
            _resolvedIp = null;
            _resolvedForHost = null;
        }
    }

    private void SetConnected(bool value)
    {
        if (_connected == value)
        {
            return;
        }

        _connected = value;
        ConnectionChanged?.Invoke(value);
    }

    private void DisposeSocket()
    {
        try { _stream?.Dispose(); } catch { /* best-effort cleanup */ }
        try { _tcpClient?.Dispose(); } catch { /* best-effort cleanup */ }
        _stream = null;
        _tcpClient = null;
        SetConnected(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _outbox.CompleteAdding();
        DisposeSocket();
        _cts.Dispose();
        _outbox.Dispose();
    }
}
