namespace Se7en.DualSense.Hid;

/// <summary>Informations sur la manette connectée.</summary>
public sealed record DualSenseDeviceInfo(
    string DevicePath,
    ConnectionType Connection,
    bool IsEdge,
    string? SerialNumber,
    int MaxInputReportLength,
    int MaxOutputReportLength)
{
    /// <summary>Adresse Bluetooth de la manette (rapport feature 0x09), ex. « A0:AB:51:12:34:56 ».</summary>
    public string? MacAddress { get; init; }

    /// <summary>Identifiant stable de la manette, identique en USB et en Bluetooth (calibration, historique).</summary>
    public string? Identity => MacAddress ?? (string.IsNullOrWhiteSpace(SerialNumber) ? null : SerialNumber);

    public string DisplayName => IsEdge ? "DualSense Edge" : "DualSense";

    public string ConnectionLabel => Connection == ConnectionType.Usb ? "USB" : "Bluetooth";
}
