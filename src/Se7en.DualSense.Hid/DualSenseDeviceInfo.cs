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
    public string DisplayName => IsEdge ? "DualSense Edge" : "DualSense";

    public string ConnectionLabel => Connection == ConnectionType.Usb ? "USB" : "Bluetooth";
}
