using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WiFiHidTrayClient;

/// <summary>One WiFi network entry: {"ssid":..., "password":..., "hidden":...}.</summary>
public sealed class PicoWifiNetwork
{
    [JsonPropertyName("ssid")]
    public string Ssid { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>True if this network doesn't broadcast its SSID; code.py only tries hidden
    /// networks once none of the broadcast ones are found by scanning.</summary>
    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }
}

/// <summary>
/// Maps to wifi_settings.json on the Pico's CIRCUITPY drive - read/written by
/// <see cref="PicoDeviceForm"/> and consumed by code.py as its only source of WiFi config.
/// </summary>
public sealed class PicoWifiSettings
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "WiFi-HID";

    [JsonPropertyName("networks")]
    public List<PicoWifiNetwork> Networks { get; set; } = new();
}
