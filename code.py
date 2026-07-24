# WiFi HID (Keyboard and Mouse) for Raspberry Pi Pico W/2W
#
# This script turns a Raspberry Pi Pico W into a USB keyboard and mouse that can be controlled over WiFi.
#
# Required libraries: Copy these from the Adafruit CircuitPython Library Bundle into your /lib folder.
# - adafruit_hid/
# - adafruit_hid/keyboard_layout_us.mpy

import time
import json
import board
import digitalio
import wifi
import mdns
import socketpool
import usb_hid

from adafruit_hid.keyboard import Keyboard
from adafruit_hid.keycode import Keycode
from adafruit_hid.mouse import Mouse
from adafruit_hid.keyboard_layout_us import KeyboardLayoutUS

# Set up the Pico as a USB HID keyboard and mouse.
kbd = Keyboard(usb_hid.devices)
layout = KeyboardLayoutUS(kbd)
mouse = Mouse(usb_hid.devices)

# Onboard LED: blinks continuously while WiFi is disconnected, and flashes 3 times fast
# whenever a client connects to the socket server (see blink_led()/blink_led_while_waiting()).
led = digitalio.DigitalInOut(board.LED)
led.direction = digitalio.Direction.OUTPUT
led.value = False


def blink_led(times, on_time=0.1, off_time=0.1):
    """Blink the onboard LED a fixed number of times (used as a one-off event indicator)."""
    for _ in range(times):
        led.value = True
        time.sleep(on_time)
        led.value = False
        time.sleep(off_time)


def blink_led_while_waiting(seconds, on_time=0.5, off_time=0.5):
    """Blink the onboard LED for about `seconds` (used while WiFi is disconnected)."""
    elapsed = 0
    while elapsed < seconds:
        led.value = True
        time.sleep(on_time)
        led.value = False
        time.sleep(off_time)
        elapsed += on_time + off_time

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
#
# WiFi credentials and the mDNS hostname are configured via wifi_settings.json in the root of
# the CIRCUITPY drive, e.g.:
#   {"hostname": "WiFi-HID", "networks": [{"ssid": "Home", "password": "pw"}]}
# Use the WiFi-HID Windows Tray Client's "Pico Settings" dialog (while the Pico is plugged in
# over USB) to create/edit this file, or write it by hand.
WIFI_SETTINGS_FILE = "wifi_settings.json"
HOSTNAME = "WiFi-HID"
networks_to_try = []

try:
    with open(WIFI_SETTINGS_FILE, "r") as f:
        wifi_settings = json.load(f)
    if isinstance(wifi_settings.get("hostname"), str) and wifi_settings["hostname"]:
        HOSTNAME = wifi_settings["hostname"]
    if isinstance(wifi_settings.get("networks"), list):
        networks_to_try = wifi_settings["networks"]
    print(f"Loaded {WIFI_SETTINGS_FILE}")
except (OSError, ValueError) as e:
    print(f"Could not load {WIFI_SETTINGS_FILE}: {e}")

if not networks_to_try:
    raise RuntimeError(
        f"No WiFi networks configured. Create {WIFI_SETTINGS_FILE} in the root of the "
        "CIRCUITPY drive (e.g. via the WiFi-HID Tray Client's Pico Settings dialog) with at "
        "least one entry in its 'networks' list."
    )

# Connect to WiFi
print("Connecting to WiFi...")
while not wifi.radio.connected:
    try:
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
            blink_led_while_waiting(10)

    except Exception as e:
        print(f"Error during WiFi connection: {e}")
        blink_led_while_waiting(10)

led.value = False

# Set up mDNS to allow discovery via WiFi-HID.local
PORT = 5005

try:
    mdns_server = mdns.Server(wifi.radio)
    mdns_server.hostname = HOSTNAME
    mdns_server.advertise_service(service_type="_wifihid", protocol="_tcp", port=PORT)
    print(f"mDNS hostname set to: {HOSTNAME}.local")
except Exception as e:
    print(f"Failed to start mDNS: {e}")

pool = socketpool.SocketPool(wifi.radio)
server_socket = pool.socket(pool.AF_INET, pool.SOCK_STREAM)
server_socket.setsockopt(pool.SOL_SOCKET, pool.SO_REUSEADDR, 1)
server_socket.bind((str(wifi.radio.ipv4_address), PORT))
server_socket.listen(1)

print("Starting socket server...")
print(f"Listening on: WiFi-HID.local:{PORT} or {wifi.radio.ipv4_address}:{PORT}")

recv_buf = bytearray(1024)

# Accept one client connection at a time. Each line sent by the client
# (newline-delimited) is treated as a single JSON command.
while True:
    try:
        conn, addr = server_socket.accept()
        print(f"Client connected: {addr}")
        blink_led(3, on_time=0.1, off_time=0.1)
        pending = ""
        try:
            while True:
                nbytes = conn.recv_into(recv_buf)
                if nbytes == 0:
                    break
                pending += recv_buf[:nbytes].decode("utf-8")
                while "\n" in pending:
                    line, pending = pending.split("\n", 1)
                    line = line.strip()
                    if line:
                        handle_request(line)
        except OSError as e:
            print(f"Connection error: {e}")
        finally:
            conn.close()
            print("Client disconnected")
    except Exception as e:
        print(f"Server error: {e}")
        time.sleep(1)