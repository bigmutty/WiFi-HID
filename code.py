# WiFi HID (Keyboard and Mouse) for Raspberry Pi Pico W/2W
#
# This script turns a Raspberry Pi Pico W into a USB keyboard and mouse that can be controlled over WiFi.
#
# Required libraries: Copy these from the Adafruit CircuitPython Library Bundle into your /lib folder.
# - adafruit_hid/
# - adafruit_httpserver/
# - adafruit_hid/keyboard_layout_us.mpy

import time
import json
import wifi
import mdns
import socketpool
import usb_hid
from adafruit_httpserver import Server as HTTPServer, Request as HTTPRequest, Response as HTTPResponse, POST

from adafruit_hid.keyboard import Keyboard
from adafruit_hid.keycode import Keycode
from adafruit_hid.mouse import Mouse
from adafruit_hid.keyboard_layout_us import KeyboardLayoutUS

# Set up the Pico as a USB HID keyboard and mouse.
kbd = Keyboard(usb_hid.devices)
layout = KeyboardLayoutUS(kbd)
mouse = Mouse(usb_hid.devices)

# Dictionary to map string key names to Keycode attributes
KEY_MAP = {k.upper(): v for k, v in Keycode.__dict__.items() if not k.startswith("__")}

MOUSE_BUTTON_MAP = {
    "LEFT": Mouse.LEFT_BUTTON,
    "RIGHT": Mouse.RIGHT_BUTTON,
    "MIDDLE": Mouse.MIDDLE_BUTTON,
}

# Function to parse the JSON request and route to the correct HID action.
def handle_request(body):
    try:
        data = json.loads(body)
        print(f"Command received: {data}")
        command = data.get("command")

        if command == "type":
            text = data.get("text", "")
            print(f"Typing: '{text}'")
            layout.write(text)

        elif command == "key":
            key_name = data.get("key", "").upper()
            action = data.get("action", "press")
            print(f"Key action: {action} {key_name}")
            if key_name in KEY_MAP:
                keycode = KEY_MAP[key_name]
                if action == "press":
                    kbd.press(keycode)
                    kbd.release(keycode)
                elif action == "keyDown":
                    kbd.press(keycode)
                elif action == "keyUp":
                    kbd.release(keycode)
                else:
                    print(f"Unknown action: {action}")
            else:
                print(f"Unknown key: {key_name}")

        elif command == "releaseAll":
            print("Releasing all keys")
            kbd.release_all()

        elif command == "mouse":
            x = int(data.get("x", 0))
            y = int(data.get("y", 0))
            wheel = int(data.get("wheel", 0))
            action = data.get("action")
            button_name = data.get("button", "").upper()

            print(f"Mouse action: x={x}, y={y}, wheel={wheel}, action={action}, button={button_name}")

            # Always process movement and scroll
            mouse.move(x=x, y=y, wheel=wheel)

            # Process button actions if specified
            if action:
                if button_name and button_name in MOUSE_BUTTON_MAP:
                    button_code = MOUSE_BUTTON_MAP[button_name]
                    if action == "click":
                        mouse.click(button_code)
                    elif action == "buttonDown":
                        mouse.press(button_code)
                    elif action == "buttonUp":
                        mouse.release(button_code)
                    else:
                        print(f"Unknown action: {action}")
                elif button_name: # button name provided but not valid
                    print(f"Unknown mouse button: {button_name}")
        else:
            print(f"Unknown command: {command}")

    except Exception as e:
        print(f"Error handling request: {e}")

# Set up WiFi and the HTTP Server.
try:
    from secrets import secrets
except ImportError:
    print("WiFi secrets are not defined in a secrets.py file.")
    raise

# Connect to WiFi
print("Connecting to WiFi...")
while not wifi.radio.connected:
    try:
        # Prepare list of networks to try
        networks_to_try = []
        if 'networks' in secrets:
            networks_to_try.extend(secrets['networks'])
        
        # Add top-level credential if not already covered or if 'networks' is missing
        if 'ssid' in secrets and 'password' in secrets:
             # Check if this ssid is already in the list
             if not any(n.get('ssid') == secrets['ssid'] for n in networks_to_try):
                networks_to_try.append({'ssid': secrets['ssid'], 'password': secrets['password']})

        if not networks_to_try:
             print("No WiFi credentials found in secrets.py")
             time.sleep(10)
             continue

        for net in networks_to_try:
            ssid = net.get('ssid')
            password = net.get('password')
            if not ssid: continue

            print(f"Connecting to {ssid}...")
            try:
                wifi.radio.connect(ssid, password)
                print("Connected!")
                break
            except Exception as e:
                print(f"Failed to connect to {ssid}: {e}")
        
        if not wifi.radio.connected:
            print("Could not connect to any network. Retrying in 10 seconds...")
            time.sleep(10)

    except Exception as e:
        print(f"Error during WiFi connection: {e}")
        time.sleep(10)

# Set up mDNS to allow access via http://WiFi-HID.local
try:
    mdns_server = mdns.Server(wifi.radio)
    mdns_server.hostname = "WiFi-HID"
    mdns_server.advertise_service(service_type="_http", protocol="_tcp", port=80)
    print(f"mDNS hostname set to: WiFi-HID.local")
except Exception as e:
    print(f"Failed to start mDNS: {e}")

pool = socketpool.SocketPool(wifi.radio)
server = HTTPServer(pool)

@server.route("/command", POST)
def command_handler(request: HTTPRequest):
    try:
        body = request.body.decode()
        handle_request(body)
        with HTTPResponse(request, content_type="application/json") as response:
            response.send('{"status": "ok"}')
    except Exception as e:
        with HTTPResponse(request, content_type="application/json") as response:
            response.send(f'{"status": "error", "message": "{str(e)}"}')

# Start the server and listen for commands.
print("Starting server...")
print(f"Listening on: http://WiFi-HID.local or http://{wifi.radio.ipv4_address}")
server.serve_forever(str(wifi.radio.ipv4_address), 80)