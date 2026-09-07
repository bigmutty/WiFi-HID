# WiFi-HID Documentation

This CircuitPython script transforms your Raspberry Pi Pico W or Pico 2W into a WiFi-controlled USB keyboard, mouse, and joystick. Control your computer remotely using the WiFiHidTrayClient Windows app, which forwards your keyboard/mouse input to the Pico over a raw TCP socket.

## Table of Contents

- [Features](#features)
- [Hardware Requirements](#hardware-requirements)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
  - [Building and running](#building-and-running)
  - [Toggling capture](#toggling-capture)
  - [Sending Ctrl+Alt+Del to the remote machine](#sending-ctrlaltdel-to-the-remote-machine)
  - [Joystick Mode](#joystick-mode)
- [Troubleshooting](#troubleshooting)
- [Advanced Usage (Expert)](#advanced-usage-expert)

## Features

- 🖱️ Control keyboard and mouse over WiFi
- ⌨️ Type text, press keys, and execute key combinations
- 🖱️ Move mouse, click buttons, and scroll
- 🕹️ Toggle a hotkey to turn mouse movement into simulated joystick/gamepad input
- 🌐 Access via mDNS hostname (`WiFi-HID.local`) or IP address
- 🔄 Automatic WiFi reconnection
- 📡 Support for multiple WiFi networks

## Hardware Requirements

- **Raspberry Pi Pico W** or **Raspberry Pi Pico 2W**
- USB cable (for power and HID connection)
- Computer with USB port

## Installation

### Step 1: Install CircuitPython

1. Download the latest CircuitPython firmware for your board:
   - [Raspberry Pi Pico W](https://circuitpython.org/board/raspberry_pi_pico_w/)
   - [Raspberry Pi Pico 2W](https://circuitpython.org/board/raspberry_pi_pico_2_w/)

2. Connect your Pico to your computer while holding the **BOOTSEL** button
3. The Pico will appear as a USB drive named `RPI-RP2`
4. Drag and drop the downloaded `.uf2` file onto the drive
5. The Pico will reboot and appear as a drive named `CIRCUITPY`

### Step 2: Install Required Libraries

`code.py` only needs the `adafruit_hid` library - it talks to clients over a raw TCP socket
using CircuitPython's built-in `wifi`/`socketpool`/`mdns` modules, not `adafruit_httpserver`.

1. Download the [Adafruit CircuitPython Library Bundle](https://circuitpython.org/libraries) matching your CircuitPython version (9.x, 8.x, etc.)
2. Extract the bundle
3. Copy the `adafruit_hid/` folder from the `lib` folder in the bundle to the `lib` folder on your `CIRCUITPY` drive

Your `CIRCUITPY/lib` folder should look like this:
```
CIRCUITPY/
├── lib/
│   └── adafruit_hid/
│       ├── __init__.mpy
│       ├── consumer_control_code.mpy
│       ├── consumer_control.mpy
│       ├── keyboard_layout_base.mpy
│       ├── keyboard_layout_us.mpy
│       ├── keyboard.mpy
│       ├── keycode.mpy
│       └── mouse.mpy
```

### Step 3: Upload the Script

1. Copy `code.py` and `boot.py` to the root of your `CIRCUITPY` drive
2. Create and configure `wifi_settings.json` (see [Configuration](#configuration) below)
3. The Pico will automatically restart and run `code.py`

`boot.py` registers the joystick USB HID device alongside the keyboard/mouse/consumer control
devices. It only runs on an actual power-up/hard reset - **not** on the auto-reload that happens
every time `code.py` is saved - so after copying `boot.py` for the first time (or editing it),
unplug and reconnect the Pico's USB cable once before the joystick will be usable.

## Configuration

Both configuration files below - `wifi_settings.json` (on the Pico) and `settings.json` (next to
the Tray Client executable) - can be created and edited entirely through the Tray Client's GUI,
with no manual JSON editing required. This is the recommended way to configure everything; see
[Managing the Pico over USB ("Pico Settings...")](#managing-the-pico-over-usb-pico-settings) and
[Client Settings ("Open Settings...")](#client-settings-open-settings) below. If you prefer to
edit the JSON files by hand instead, see the [Advanced Usage (Expert)](#advanced-usage-expert)
section.

### Managing the Pico over USB ("Pico Settings...")

When the Pico is connected to the tray client's PC over USB (mounted as its `CIRCUITPY` drive),
a **Pico Settings...** item appears in the tray menu (checked every couple of seconds, so it
shows up/disappears automatically as you plug/unplug the board). It opens a dialog to edit:

- **Hostname** - the mDNS name the Pico advertises (`<hostname>.local`).
- **WiFi networks** - a list of SSID/password profiles, tried in order at boot. Click **Add
  Current Network** to automatically add (or update the saved password for) the WiFi network
  this PC is currently connected to, instead of typing the SSID/password in by hand.

Saving writes `wifi_settings.json` to the root of the `CIRCUITPY` drive, which is the only
source of WiFi credentials and hostname that `code.py` reads - at least one network is required.
Because CircuitPython auto-reloads on any file change to the drive, the Pico restarts `code.py`
and reconnects using the new settings right away.

### Client Settings ("Open Settings...")

The tray menu's **Open Settings...** dialog edits the Tray Client's own `settings.json`
(created next to the executable on first run) - the host/port to connect to, and all of the
hotkeys (capture toggle, Ctrl+Alt+Del, Joystick Mode). It validates the host and key names and
applies changes immediately: it reconnects to the new host/port and refreshes all hotkeys on the
fly, no restart required.

### Finding Your Device's IP Address

After the Pico boots and connects to WiFi, you can find its IP address by:

1. **Check the serial console**: Connect via a serial terminal (9600 baud) to see startup messages
2. **Check your router's DHCP client list**: Look for a device named `WiFi-HID` or with the Pico's MAC address
3. **Use mDNS discovery tools**: Look for `WiFi-HID.local` on your network

The console output will show:
```
Connecting to WiFi...
Connecting to HomeNetwork...
Connected!
mDNS hostname set to: WiFi-HID.local
Starting server...
Listening on: WiFi-HID.local or 192.168.1.100 (port 5005)
```

## Usage

WiFi-HID doesn't expose an HTTP API - the Pico only speaks a raw, newline-delimited JSON
protocol over a plain TCP socket (port 5005). The intended way to drive it is the
**WiFiHidTrayClient** Windows app below, which captures your PC's own keyboard/mouse input and
forwards it to the Pico over that socket, rather than requiring you to write any client code.

`WiFiHidTrayClient/` is a .NET 8 WinForms system tray application that turns any Windows PC
into a "KVM console" for the Pico. When capturing is enabled it installs global low-level
keyboard/mouse hooks so **every keystroke and mouse event is intercepted and forwarded to the
Pico over the raw TCP socket on port 5005, and blocked from
reaching the rest of the local system** (apps, shortcuts, the taskbar, etc. see nothing).

### Building and running

```powershell
cd WiFiHidTrayClient
dotnet build -c Release
dotnet run -c Release
```

The compiled `WiFiHidTrayClient.exe` (in `bin/Release/net8.0-windows/`) can be copied anywhere
and run standalone - it's a normal tray app with no installer.

### Toggling capture

Press **Ctrl+Alt+Shift+F12** (configurable via [Open Settings...](#client-settings-open-settings))
to switch capturing on/off - this combo is recognized directly inside the hook and is never
forwarded anywhere, so it always works even while every other key/click is being captured. The
tray icon and a balloon tip indicate the current state; double-clicking the tray icon also
toggles it.

### Sending Ctrl+Alt+Del to the remote machine

The real Ctrl+Alt+Delete is a Secure Attention Sequence handled directly by Winlogon - Windows
never delivers it to any hook, so it can't be captured and forwarded like other keys. Instead,
press **Ctrl+Alt+Shift+Delete** while capturing is active to send a virtual Ctrl+Alt+Del to the
Pico (Control down, Alt down, Delete press, Alt up, Control up). It's also available any time
from the tray menu as "Send Ctrl+Alt+Del", regardless of capture state. Both hotkeys are
configurable via [Open Settings...](#client-settings-open-settings).

**Safety net:** if the app ever misbehaves, `Ctrl+Alt+Delete` is handled by Windows itself
(via Winlogon) and cannot be intercepted by any user-mode hook, so it always remains available
to open Task Manager and end the tray client's process.

### Joystick Mode

Press **Ctrl+Alt+Shift+J** (configurable via [Open Settings...](#client-settings-open-settings))
while capturing is active to toggle Joystick Mode, or use "Start/Stop Joystick Mode" from the
tray menu. While Joystick Mode is on:

- Mouse movement is translated into joystick X/Y axis input instead of moving the cursor. The
  stick automatically springs back to center when you stop moving the mouse.
- Left and right mouse buttons become joystick buttons 1 and 2.
- The middle button and scroll wheel are ignored (there's no joystick equivalent).
- Keyboard input is unaffected and keeps forwarding normally.

Turning Joystick Mode off (via the hotkey, the tray menu, or automatically when capturing itself
is stopped) releases the stick back to a centered, no-buttons-pressed state.

The tray icon tooltip shows a `JOYSTICK` suffix while active, and a balloon tip announces each
toggle.

**Requires `boot.py`:** the joystick only exists as a USB device if `boot.py` has been copied to
the `CIRCUITPY` drive and the Pico has been power-cycled at least once afterward (see
[Step 3: Upload the Script](#step-3-upload-the-script)). If the Pico's serial console prints
`Joystick HID device not found`, `boot.py` hasn't taken effect yet - power-cycle the board.

## Troubleshooting

### Pico Won't Connect to WiFi

1. Check your `wifi_settings.json` file for typos in SSID or password
2. Ensure your WiFi is 2.4GHz (Pico W doesn't support 5GHz)
3. Check the serial console for error messages
4. Try resetting the Pico by unplugging and reconnecting it

### Can't Access via WiFi-HID.local

1. Ensure your computer supports mDNS:
   - **Windows**: Install Bonjour Print Services or iTunes
   - **macOS/Linux**: Built-in support
2. Try using the IP address directly instead (faster anyway!)
3. Check firewall settings

### Keys Not Working

1. Ensure the Pico is enumerated as a USB HID device
2. Try unplugging and reconnecting the USB cable
3. Check that the key name matches the available keys (case-insensitive)
4. Use `releaseAll` to reset stuck keys

### Mouse Not Moving

1. Verify the Pico is recognized as a USB mouse
2. Remember: x and y values are relative movements (range: -127 to 127)
3. For large movements, send multiple commands

### Server Not Responding

1. Check the serial console for errors
2. Verify the Pico is still connected to WiFi
3. Try pinging the IP address
4. Reset the Pico

### Slow Response Times

1. **Use the IP address** instead of `WiFi-HID.local` hostname
2. Reduce WiFi interference
3. Move the Pico closer to your WiFi router
4. Check network congestion

### Joystick Not Working

1. Confirm `boot.py` has been copied to the root of the `CIRCUITPY` drive
2. Power-cycle the Pico (unplug/replug USB) - `boot.py` only runs on an actual boot, not on the
   `code.py` auto-reload
3. Check the serial console for `Joystick HID device not found` - if you see it, the power cycle
   above hasn't happened yet
4. Verify Joystick Mode is actually toggled on (tray tooltip shows `JOYSTICK`) and that capturing
   is active - the hotkey only works while capturing

## Advanced Usage (Expert)

The sections below cover editing the two JSON configuration files by hand instead of through the
Tray Client's GUI ([Managing the Pico over USB](#managing-the-pico-over-usb-pico-settings) and
[Client Settings](#client-settings-open-settings)). This isn't necessary for normal use, but can
be useful for scripting, backups, or troubleshooting.

### Manually Editing wifi_settings.json

Create or edit a `wifi_settings.json` file in the root of your `CIRCUITPY` drive with your WiFi
credentials and (optionally) a custom mDNS hostname. The script tries each network in the
`networks` list in order until one connects.

```json
{
  "hostname": "WiFi-HID",
  "networks": [
    {"ssid": "HomeNetwork", "password": "homepassword123"},
    {"ssid": "WorkNetwork", "password": "workpass456"},
    {"ssid": "MobileHotspot", "password": "mobile789"}
  ]
}
```

**Important Notes:**
- Replace the `ssid`/`password` values with your actual WiFi credentials
- The file must be named exactly `wifi_settings.json` and contain valid JSON
- At least one entry in `networks` is required - `code.py` will refuse to start without one
- This file contains sensitive information - keep it secure!
- Because CircuitPython auto-reloads on any file change to the drive, saving this file restarts
  `code.py` and reconnects using the new settings right away

### Manually Editing settings.json

The Tray Client's `settings.json` (created next to the executable on first run) can be edited
directly instead of through the **Open Settings...** dialog:

```json
{
  "Host": "WiFi-HID.local",
  "Port": 5005,
  "ToggleRequiresControl": true,
  "ToggleRequiresAlt": true,
  "ToggleRequiresShift": true,
  "ToggleKey": "F12",
  "SendCtrlAltDelRequiresControl": true,
  "SendCtrlAltDelRequiresAlt": true,
  "SendCtrlAltDelRequiresShift": true,
  "SendCtrlAltDelKey": "Delete",
  "JoystickRequiresControl": true,
  "JoystickRequiresAlt": true,
  "JoystickRequiresShift": true,
  "JoystickToggleKey": "J"
}
```

`Host` can be either the Pico's IP address or its mDNS hostname (e.g. `WiFi-HID.local`). If it's
a hostname, the client pings it once to discover its current IP and caches that IP for all
subsequent (re)connects, so you don't pay the mDNS resolution cost on every reconnect. If a
connection using the cached IP ever fails (e.g. the Pico got a new DHCP lease), the client
automatically re-pings the hostname to pick up the new address on the next attempt.

Edits made by hand while the Tray Client is running aren't picked up automatically - use
**Open Settings...** (even just clicking OK without changes) or restart the app to apply them.

### Setting Static IP

Modify `code.py` to set a static IP (add after WiFi connection):

```python
import ipaddress

wifi.radio.set_ipv4_address(
    ipv4=ipaddress.IPv4Address("192.168.1.50"),
    netmask=ipaddress.IPv4Address("255.255.255.0"),
    gateway=ipaddress.IPv4Address("192.168.1.1"),
    dns=ipaddress.IPv4Address("8.8.8.8")
)
```

### Custom Hostname

Set a custom mDNS hostname via the `"hostname"` field in `wifi_settings.json` (edit it directly,
or use the Tray Client's [Pico Settings dialog](#managing-the-pico-over-usb-pico-settings)) -
no code changes needed.

### Security Considerations

This script has **no authentication** by default. Anyone on your network can send commands. For security:

1. Use on trusted networks only
2. Consider adding authentication to the TCP socket handler in `code.py`
3. Use a firewall to restrict access
4. Don't expose to the internet

## License

This project uses CircuitPython and Adafruit libraries. Please refer to their respective licenses.

## Support

For issues related to:
- **CircuitPython**: [CircuitPython GitHub](https://github.com/adafruit/circuitpython)
- **Adafruit Libraries**: [Adafruit CircuitPython Bundle](https://github.com/adafruit/Adafruit_CircuitPython_Bundle)
- **This Script**: Check the code comments and this documentation

---

**Happy automating! 🚀**
