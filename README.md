# WiFi-HID DDocumentation

This CircuitPython script transforms your Raspberry Pi Pico W or Pico 2W into a WiFi-controlled USB keyboard and mouse. Control your computer remotely by sending HTTP requests to the Pico.

## Table of Contents

- [Features](#features)
- [Hardware Requirements](#hardware-requirements)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [API Reference](#api-reference)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

## Features

- 🖱️ Control keyboard and mouse over WiFi
- ⌨️ Type text, press keys, and execute key combinations
- 🖱️ Move mouse, click buttons, and scroll
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

1. Download the [Adafruit CircuitPython Library Bundle](https://circuitpython.org/libraries) matching your CircuitPython version (9.x, 8.x, etc.)
2. Extract the bundle
3. Copy the following folders/files from the `lib` folder in the bundle to the `lib` folder on your `CIRCUITPY` drive:
   - `adafruit_hid/` (entire folder)
   - `adafruit_httpserver/` (entire folder)

Your `CIRCUITPY/lib` folder should look like this:
```
CIRCUITPY/
├── lib/
│   ├── adafruit_hid/
│   │   ├── __init__.mpy
│   │   ├── consumer_control_code.mpy
│   │   ├── consumer_control.mpy
│   │   ├── keyboard_layout_base.mpy
│   │   ├── keyboard_layout_us.mpy
│   │   ├── keyboard.mpy
│   │   ├── keycode.mpy
│   │   └── mouse.mpy
│   └── adafruit_httpserver/
│       ├── __init__.mpy
│       ├── authentication.mpy
│       ├── exceptions.mpy
│       ├── headers.mpy
│       ├── interfaces.mpy
│       ├── methods.mpy
│       ├── mime_types.mpy
│       ├── request.mpy
│       ├── response.mpy
│       ├── route.mpy
│       ├── server.mpy
│       └── status.mpy
```

### Step 3: Upload the Script

1. Copy `code.py` to the root of your `CIRCUITPY` drive
2. Create and configure `secrets.py` (see [Configuration](#configuration) below)
3. The Pico will automatically restart and run the script

## Configuration

### Creating secrets.py

Create a `secrets.py` file in the root of your `CIRCUITPY` drive with your WiFi credentials.

#### Option 1: Single Network

```python
secrets = {
    'ssid': 'YourWiFiName',
    'password': 'YourWiFiPassword'
}
```

#### Option 2: Multiple Networks (Recommended)

The script will try each network in order until it successfully connects:

```python
secrets = {
    # Default network (optional - will be added to the list if not already present)
    'ssid': 'HomeNetwork',
    'password': 'homepassword123',
    
    # List of all networks to try
    'networks': [
        {'ssid': 'HomeNetwork', 'password': 'homepassword123'},
        {'ssid': 'WorkNetwork', 'password': 'workpass456'},
        {'ssid': 'MobileHotspot', 'password': 'mobile789'},
    ]
}
```

**Important Notes:**
- Replace `YourWiFiName` and `YourWiFiPassword` with your actual WiFi credentials
- Use single quotes for strings containing apostrophes: `'Bob\'s Network'`
- The file must be named exactly `secrets.py`
- This file contains sensitive information - keep it secure!

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
Listening on: http://WiFi-HID.local or http://192.168.1.100
```

## Usage

### Accessing the Device

You can control the device using either:

1. **IP Address** (recommended - much faster): `http://192.168.1.100/command`
2. **mDNS Hostname**: `http://WiFi-HID.local/command`

**Note:** Using the IP address is significantly faster than using the hostname because it bypasses mDNS resolution. For best performance, use the IP address directly.

### Making Requests

Send POST requests to the `/command` endpoint with JSON payloads describing the action to perform.

**Endpoint:** `POST /command`

**Content-Type:** `application/json`

## API Reference

### Commands Overview

| Command | Description |
|---------|-------------|
| `type` | Type text as if typed on a keyboard |
| `key` | Press, hold, or release specific keys |
| `releaseAll` | Release all currently pressed keys |
| `mouse` | Move mouse, click buttons, or scroll |

---

### 1. Type Text

Types text as if you were typing on a keyboard.

**Parameters:**
- `command`: `"type"` (required)
- `text`: String to type (required)

**Example:**
```json
{
  "command": "type",
  "text": "Hello, World!"
}
```

---

### 2. Key Actions

Press individual keys or key combinations.

**Parameters:**
- `command`: `"key"` (required)
- `key`: Key name (required) - see [Available Keys](#available-keys)
- `action`: `"press"` (default), `"keyDown"`, or `"keyUp"` (optional)

**Actions:**
- `press`: Press and release immediately (default)
- `keyDown`: Press and hold the key
- `keyUp`: Release a previously held key

**Example:**
```json
{
  "command": "key",
  "key": "ENTER",
  "action": "press"
}
```

---

### 3. Release All Keys

Releases all currently pressed keys. Useful for recovering from stuck modifier keys.

**Parameters:**
- `command`: `"releaseAll"` (required)

**Example:**
```json
{
  "command": "releaseAll"
}
```

---

### 4. Mouse Control

Control mouse movement, clicks, and scrolling.

**Parameters:**
- `command`: `"mouse"` (required)
- `x`: Horizontal movement in pixels (optional, default: 0, range: -127 to 127)
- `y`: Vertical movement in pixels (optional, default: 0, range: -127 to 127)
- `wheel`: Scroll amount (optional, default: 0, negative = scroll down, positive = scroll up)
- `action`: `"click"`, `"buttonDown"`, or `"buttonUp"` (optional)
- `button`: `"LEFT"`, `"RIGHT"`, or `"MIDDLE"` (required if action is specified)

**Mouse Buttons:**
- `LEFT`: Left mouse button
- `RIGHT`: Right mouse button
- `MIDDLE`: Middle mouse button

**Example:**
```json
{
  "command": "mouse",
  "x": 100,
  "y": -50,
  "wheel": 0,
  "action": "click",
  "button": "LEFT"
}
```

---

### Available Keys

The following keys are available for the `key` command:

**Letters:** A-Z

**Numbers:** ZERO through NINE (or 0-9)

**Function Keys:** F1-F24

**Modifiers:**
- `SHIFT`, `LEFT_SHIFT`, `RIGHT_SHIFT`
- `CONTROL`, `LEFT_CONTROL`, `RIGHT_CONTROL`
- `ALT`, `LEFT_ALT`, `RIGHT_ALT`
- `GUI`, `LEFT_GUI`, `RIGHT_GUI` (Windows key / Command key)

**Navigation:**
- `UP_ARROW`, `DOWN_ARROW`, `LEFT_ARROW`, `RIGHT_ARROW`
- `PAGE_UP`, `PAGE_DOWN`
- `HOME`, `END`

**Special Keys:**
- `ENTER`, `RETURN`
- `ESCAPE`
- `BACKSPACE`
- `TAB`
- `SPACE`, `SPACEBAR`
- `DELETE`
- `INSERT`
- `CAPS_LOCK`
- `PRINT_SCREEN`
- `SCROLL_LOCK`
- `PAUSE`

**Punctuation:**
- `PERIOD`, `COMMA`
- `FORWARD_SLASH`, `BACKSLASH`
- `SEMICOLON`, `QUOTE`
- `LEFT_BRACKET`, `RIGHT_BRACKET`
- `MINUS`, `EQUALS`
- `GRAVE_ACCENT` (backtick)

(And more - check the CircuitPython HID documentation for a complete list)

## Examples

### Using cURL

#### Example 1: Type Text
```bash
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"type","text":"Hello from my Pico!"}'
```

#### Example 2: Press Enter
```bash
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"ENTER"}'
```

#### Example 3: Open Run Dialog (Windows Key + R)
```bash
# Press Windows key down
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"GUI","action":"keyDown"}'

# Press R
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"R","action":"press"}'

# Release Windows key
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"GUI","action":"keyUp"}'
```

#### Example 4: Copy Text (Ctrl+C)
```bash
# Press Ctrl down
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"CONTROL","action":"keyDown"}'

# Press C
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"C","action":"press"}'

# Release Ctrl
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"key","key":"CONTROL","action":"keyUp"}'
```

#### Example 5: Move Mouse
```bash
# Move mouse 100 pixels right and 50 pixels down
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","x":100,"y":50}'
```

#### Example 6: Left Click
```bash
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","action":"click","button":"LEFT"}'
```

#### Example 7: Right Click
```bash
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","action":"click","button":"RIGHT"}'
```

#### Example 8: Scroll Down
```bash
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","wheel":-5}'
```

#### Example 9: Drag and Drop
```bash
# Move to start position
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","x":100,"y":100}'

# Press left button down
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","action":"buttonDown","button":"LEFT"}'

# Move while holding
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","x":200,"y":0}'

# Release button
curl -X POST http://192.168.1.100/command \
  -H "Content-Type: application/json" \
  -d '{"command":"mouse","action":"buttonUp","button":"LEFT"}'
```

---

### Using Python

#### Example 1: Simple Type Function
```python
import requests

PICO_IP = "192.168.1.100"  # Use IP for faster response
PICO_URL = f"http://{PICO_IP}/command"

def type_text(text):
    response = requests.post(PICO_URL, json={"command": "type", "text": text})
    return response.json()

# Usage
type_text("Hello from Python!")
```

#### Example 2: Press Key Function
```python
import requests

PICO_IP = "192.168.1.100"
PICO_URL = f"http://{PICO_IP}/command"

def press_key(key, action="press"):
    response = requests.post(PICO_URL, json={
        "command": "key",
        "key": key.upper(),
        "action": action
    })
    return response.json()

# Usage
press_key("ENTER")
press_key("SHIFT", "keyDown")
press_key("A", "press")
press_key("SHIFT", "keyUp")
```

#### Example 3: Keyboard Shortcut Helper
```python
import requests
import time

PICO_IP = "192.168.1.100"
PICO_URL = f"http://{PICO_IP}/command"

def keyboard_shortcut(*keys):
    """Press a keyboard shortcut (e.g., Ctrl+C, Alt+Tab)"""
    # Press all keys down
    for key in keys[:-1]:
        requests.post(PICO_URL, json={"command": "key", "key": key.upper(), "action": "keyDown"})
        time.sleep(0.05)
    
    # Press and release the final key
    requests.post(PICO_URL, json={"command": "key", "key": keys[-1].upper(), "action": "press"})
    time.sleep(0.05)
    
    # Release modifier keys in reverse order
    for key in reversed(keys[:-1]):
        requests.post(PICO_URL, json={"command": "key", "key": key.upper(), "action": "keyUp"})
        time.sleep(0.05)

# Usage
keyboard_shortcut("CONTROL", "C")  # Ctrl+C
keyboard_shortcut("CONTROL", "SHIFT", "ESC")  # Ctrl+Shift+Esc (Task Manager)
keyboard_shortcut("GUI", "L")  # Windows+L (Lock screen)
```

#### Example 4: Mouse Control Functions
```python
import requests

PICO_IP = "192.168.1.100"
PICO_URL = f"http://{PICO_IP}/command"

def move_mouse(x, y):
    """Move mouse by x, y pixels"""
    response = requests.post(PICO_URL, json={
        "command": "mouse",
        "x": x,
        "y": y
    })
    return response.json()

def mouse_click(button="LEFT"):
    """Click a mouse button"""
    response = requests.post(PICO_URL, json={
        "command": "mouse",
        "action": "click",
        "button": button.upper()
    })
    return response.json()

def mouse_scroll(amount):
    """Scroll the mouse wheel (negative = down, positive = up)"""
    response = requests.post(PICO_URL, json={
        "command": "mouse",
        "wheel": amount
    })
    return response.json()

# Usage
move_mouse(100, -50)
mouse_click("LEFT")
mouse_click("RIGHT")
mouse_scroll(-3)  # Scroll down
```

#### Example 5: Complete Automation Script
```python
import requests
import time

PICO_IP = "192.168.1.100"  # Much faster than using WiFi-HID.local
PICO_URL = f"http://{PICO_IP}/command"

def send_command(payload):
    """Send a command to the Pico"""
    response = requests.post(PICO_URL, json=payload, timeout=2)
    return response.json()

# Open Notepad on Windows
send_command({"command": "key", "key": "GUI", "action": "keyDown"})
time.sleep(0.1)
send_command({"command": "key", "key": "R", "action": "press"})
time.sleep(0.1)
send_command({"command": "key", "key": "GUI", "action": "keyUp"})
time.sleep(0.5)

# Type "notepad" and press Enter
send_command({"command": "type", "text": "notepad"})
time.sleep(0.2)
send_command({"command": "key", "key": "ENTER"})
time.sleep(1)

# Type some text
send_command({"command": "type", "text": "This is automated via WiFi HID!\n"})
send_command({"command": "type", "text": "Pretty cool, right?"})
```

---

### Using JavaScript (Node.js)

```javascript
const axios = require('axios');

const PICO_IP = '192.168.1.100';  // IP address is faster!
const PICO_URL = `http://${PICO_IP}/command`;

async function typeText(text) {
  const response = await axios.post(PICO_URL, {
    command: 'type',
    text: text
  });
  return response.data;
}

async function pressKey(key, action = 'press') {
  const response = await axios.post(PICO_URL, {
    command: 'key',
    key: key.toUpperCase(),
    action: action
  });
  return response.data;
}

async function moveMouse(x, y) {
  const response = await axios.post(PICO_URL, {
    command: 'mouse',
    x: x,
    y: y
  });
  return response.data;
}

async function mouseClick(button = 'LEFT') {
  const response = await axios.post(PICO_URL, {
    command: 'mouse',
    action: 'click',
    button: button.toUpperCase()
  });
  return response.data;
}

// Usage
(async () => {
  await typeText('Hello from JavaScript!');
  await pressKey('ENTER');
  await moveMouse(100, 100);
  await mouseClick('LEFT');
})();
```

---

### Using PowerShell (Windows)

```powershell
# Set variables
$PicoIP = "192.168.1.100"  # Faster than hostname
$PicoURL = "http://$PicoIP/command"

# Type text
$body = @{
    command = "type"
    text = "Hello from PowerShell!"
} | ConvertTo-Json

Invoke-RestMethod -Uri $PicoURL -Method Post -Body $body -ContentType "application/json"

# Press Enter
$body = @{
    command = "key"
    key = "ENTER"
} | ConvertTo-Json

Invoke-RestMethod -Uri $PicoURL -Method Post -Body $body -ContentType "application/json"

# Move mouse
$body = @{
    command = "mouse"
    x = 100
    y = 50
} | ConvertTo-Json

Invoke-RestMethod -Uri $PicoURL -Method Post -Body $body -ContentType "application/json"

# Click left mouse button
$body = @{
    command = "mouse"
    action = "click"
    button = "LEFT"
} | ConvertTo-Json

Invoke-RestMethod -Uri $PicoURL -Method Post -Body $body -ContentType "application/json"
```

---

## Performance Tips

### Use IP Address Instead of Hostname

For the best performance, **always use the IP address** instead of the mDNS hostname (`WiFi-HID.local`). 

**Why?** mDNS resolution adds latency to every request. Using the IP address directly can be 50-200ms faster per request, which is significant for real-time control.

**Slow:**
```python
PICO_URL = "http://WiFi-HID.local/command"  # mDNS lookup on every request
```

**Fast:**
```python
PICO_URL = "http://192.168.1.100/command"  # Direct connection
```

### Batch Operations

For complex operations, send commands in quick succession rather than waiting for responses:

```python
import requests
from concurrent.futures import ThreadPoolExecutor

PICO_URL = "http://192.168.1.100/command"

commands = [
    {"command": "type", "text": "Hello "},
    {"command": "type", "text": "World"},
    {"command": "key", "key": "ENTER"},
]

# Send all commands quickly
with ThreadPoolExecutor(max_workers=3) as executor:
    results = list(executor.map(lambda cmd: requests.post(PICO_URL, json=cmd), commands))
```

## Troubleshooting

### Pico Won't Connect to WiFi

1. Check your `secrets.py` file for typos in SSID or password
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

## Advanced Usage

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

Change the mDNS hostname by modifying this line in `code.py`:

```python
mdns_server.hostname = "WiFi-HID"  # Change to your preferred name
```

### Security Considerations

This script has **no authentication** by default. Anyone on your network can send commands. For security:

1. Use on trusted networks only
2. Consider implementing authentication in the HTTP handler
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
