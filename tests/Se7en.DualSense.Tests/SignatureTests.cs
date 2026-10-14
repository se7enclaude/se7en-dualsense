using Se7en.DualSense.Output;
using Xunit;

namespace Se7en.DualSense.Tests;

public class SignatureTests
{
    [Fact]
    public void Unsigned_file_is_rejected()
    {
        string f = Path.Combine(Path.GetTempPath(), "se7en-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(f, new byte[] { 0x4D, 0x5A, 0, 0 });
        try { Assert.False(DriverInstaller.IsSignedByNefarius(f)); }
        finally { File.Delete(f); }
    }

    /// <summary>Installeur officiel (certificat expiré mais signature horodatée) : variable SE7EN_SIGNED_INSTALLER.</summary>
    [Fact]
    public void Timestamped_nefarius_installer_is_accepted_and_tampering_is_detected()
    {
        string? path = Environment.GetEnvironmentVariable("SE7EN_SIGNED_INSTALLER");
        if (path == null || !File.Exists(path)) return;
        Assert.True(DriverInstaller.IsSignedByNefarius(path));

        string copy = Path.Combine(Path.GetTempPath(), "se7en-tampered-" + Guid.NewGuid().ToString("N") + ".exe");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF; // un octet modifié au milieu du programme
        File.WriteAllBytes(copy, bytes);
        try { Assert.False(DriverInstaller.IsSignedByNefarius(copy)); }
        finally { File.Delete(copy); }
    }
}
