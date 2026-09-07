# Runs once at actual boot (power-up/hard reset) - NOT on every code.py auto-reload - to
# register a custom joystick HID device alongside the default keyboard/mouse/consumer control
# devices. After copying this file to the CIRCUITPY drive for the first time (or editing it),
# you must fully power-cycle or hard-reset the Pico (not just save code.py) for it to take effect.
import usb_hid

# 2-axis, 2-button joystick: report = [buttons (bit0=button1, bit1=button2), x (-127..127), y (-127..127)].
# Report ID 4 avoids colliding with the default KEYBOARD(1)/MOUSE(2)/CONSUMER_CONTROL(3) devices.
JOYSTICK_REPORT_DESCRIPTOR = bytes((
    0x05, 0x01,  # Usage Page (Generic Desktop Ctrls)
    0x09, 0x04,  # Usage (Joystick)
    0xA1, 0x01,  # Collection (Application)
    0x85, 0x04,  #   Report ID (4)
    0xA1, 0x00,  #   Collection (Physical)
    0x05, 0x09,  #     Usage Page (Button)
    0x19, 0x01,  #     Usage Minimum (Button 1)
    0x29, 0x02,  #     Usage Maximum (Button 2)
    0x15, 0x00,  #     Logical Minimum (0)
    0x25, 0x01,  #     Logical Maximum (1)
    0x75, 0x01,  #     Report Size (1)
    0x95, 0x02,  #     Report Count (2)
    0x81, 0x02,  #     Input (Data,Var,Abs)
    0x75, 0x06,  #     Report Size (6)
    0x95, 0x01,  #     Report Count (1)
    0x81, 0x03,  #     Input (Const,Var,Abs) - 6 bits padding so buttons fill a whole byte
    0x05, 0x01,  #     Usage Page (Generic Desktop Ctrls)
    0x09, 0x30,  #     Usage (X)
    0x09, 0x31,  #     Usage (Y)
    0x15, 0x81,  #     Logical Minimum (-127)
    0x25, 0x7F,  #     Logical Maximum (127)
    0x75, 0x08,  #     Report Size (8)
    0x95, 0x02,  #     Report Count (2)
    0x81, 0x02,  #     Input (Data,Var,Abs)
    0xC0,        #   End Collection
    0xC0,        # End Collection
))

joystick_device = usb_hid.Device(
    report_descriptor=JOYSTICK_REPORT_DESCRIPTOR,
    usage_page=0x01,
    usage=0x04,
    report_ids=(4,),
    in_report_lengths=(3,),
    out_report_lengths=(0,),
)

usb_hid.enable(
    (usb_hid.Device.KEYBOARD, usb_hid.Device.MOUSE, usb_hid.Device.CONSUMER_CONTROL, joystick_device)
)
