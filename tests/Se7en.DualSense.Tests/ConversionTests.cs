using Se7en.DualSense.Hid;
using Se7en.DualSense.Output;
using Xunit;

namespace Se7en.DualSense.Tests;

public class DualSenseParserTests
{
    /// <summary>Rapport USB réel capturé sur une DualSense au repos, en charge, batterie quasi vide.</summary>
    private static readonly byte[] RealUsbReport = Convert.FromHexString(
        "01838783800000E40800000066BAA03F" +
        "FEFF0400FFFFD600CA1F250483142E1A" +
        "1789C9E03F8A668732A6090900000000" +
        "00222C2E1A100800C98018 78EA7B1B7A".Replace(" ", ""));

    [Fact]
    public void Parses_real_usb_report()
    {
        Assert.True(DualSenseReportParser.TryParse(RealUsbReport, out var s));
        Assert.True(s.IsFullReport);
        Assert.Equal(0x83, s.LeftX);
        Assert.Equal(0x87, s.LeftY);
        Assert.Equal(0x83, s.RightX);
        Assert.Equal(0x80, s.RightY);
        Assert.Equal(DualSenseButtons.None, s.Buttons);
        Assert.Equal(0x1FCA, s.AccelY); // gravité ≈ 8192
        Assert.False(s.Touch0.Active);
        Assert.False(s.Touch1.Active);
        Assert.Equal(ChargingState.Charging, s.Charging);
        Assert.Equal(5, s.BatteryPercent);
    }

    [Theory]
    [InlineData(0x08, 0x00, 0x00, DualSenseButtons.None)]
    [InlineData(0x00, 0x00, 0x00, DualSenseButtons.DpadUp)]
    [InlineData(0x03, 0x00, 0x00, DualSenseButtons.DpadDown | DualSenseButtons.DpadRight)]
    [InlineData(0x28, 0x00, 0x00, DualSenseButtons.Cross)]
    [InlineData(0x88, 0x00, 0x00, DualSenseButtons.Triangle)]
    [InlineData(0x08, 0x21, 0x00, DualSenseButtons.L1 | DualSenseButtons.Options)]
    [InlineData(0x08, 0x00, 0x07, DualSenseButtons.PS | DualSenseButtons.Touchpad | DualSenseButtons.Mute)]
    public void Decodes_buttons(byte b0, byte b1, byte b2, DualSenseButtons expected)
    {
        Assert.Equal(expected, DualSenseReportParser.DecodeButtons(b0, b1, b2));
    }

    [Fact]
    public void Bluetooth_report_with_bad_crc_is_rejected()
    {
        var report = new byte[DualSenseReportParser.BtInputReportLength];
        report[0] = 0x31;
        Assert.False(DualSenseReportParser.TryParse(report, out _));
    }

    [Fact]
    public void Bluetooth_output_report_has_valid_crc()
    {
        var buffer = new byte[DualSenseOutputReport.BtLength];
        int len = DualSenseOutputReport.Build(buffer, ConnectionType.Bluetooth, DualSenseOutputState.Default, 3, false);
        Assert.Equal(78, len);
        Assert.Equal(0x31, buffer[0]);
        Assert.Equal(0x30, buffer[1]); // séquence 3 << 4
        uint crc = Crc32.Compute(Crc32.OutputSeed, buffer.AsSpan(0, 74));
        Assert.Equal(crc, BitConverter.ToUInt32(buffer, 74));
    }

    [Fact]
    public void Crc32_matches_reference_value()
    {
        // CRC32 IEEE de "123456789" = 0xCBF43926 ; ici précédé d'un octet seed, on vérifie la cohérence
        byte[] data = "123456789"u8.ToArray();
        uint withSeed = Crc32.Compute((byte)'1', data.AsSpan(1));
        Assert.Equal(0xCBF43926u, withSeed);
    }
}

public class Ds4ConversionTests
{
    [Fact]
    public void Real_state_is_copied_to_ds4_report()
    {
        var state = new DualSenseState
        {
            LeftX = 0x83, LeftY = 0x87, RightX = 0x10, RightY = 0xF0, L2 = 12, R2 = 250,
            Buttons = DualSenseButtons.Cross | DualSenseButtons.R1 | DualSenseButtons.PS | DualSenseButtons.Touchpad,
            BatteryPercent = 100, Charging = ChargingState.Full, IsFullReport = true,
            Touch0 = new TouchPoint(true, 5, 1919, 1079),
        };
        var report = new byte[Ds4ReportBuilder.Length];
        new Ds4ReportBuilder().Build(state, report);

        Assert.Equal(new byte[] { 0x83, 0x87, 0x10, 0xF0 }, report[..4]);
        ushort buttons = BitConverter.ToUInt16(report, 4);
        Assert.Equal(8, buttons & 0x0F);                // croix directionnelle au neutre
        Assert.NotEqual(0, buttons & (1 << 5));         // ✕
        Assert.NotEqual(0, buttons & (1 << 9));         // R1
        Assert.Equal(0x03, report[6]);                  // PS + clic pavé
        Assert.Equal(12, report[7]);
        Assert.Equal(250, report[8]);
        Assert.Equal(0x1A, report[29]);                 // niveau 10 + câble

        // Doigt : actif (bit 7 à 0), id 5, x = 1919, y ramené à l'échelle DS4 (942)
        Assert.Equal(5, report[34]);
        int x = report[35] | ((report[36] & 0x0F) << 8);
        int y = (report[36] >> 4) | (report[37] << 4);
        Assert.Equal(1919, x);
        Assert.Equal(942, y);
    }

    [Theory]
    [InlineData(DualSenseButtons.DpadUp, 0)]
    [InlineData(DualSenseButtons.DpadUp | DualSenseButtons.DpadRight, 1)]
    [InlineData(DualSenseButtons.DpadRight, 2)]
    [InlineData(DualSenseButtons.DpadDown, 4)]
    [InlineData(DualSenseButtons.DpadLeft | DualSenseButtons.DpadUp, 7)]
    [InlineData(DualSenseButtons.None, 8)]
    public void Dpad_is_encoded_as_hat(DualSenseButtons dpad, int hat)
    {
        Assert.Equal(hat, Ds4ReportBuilder.EncodeButtons(dpad) & 0x0F);
    }

    [Fact]
    public void Game_output_report_gives_rumble_and_lightbar()
    {
        byte[] report = { 0x05, 0x03, 0x00, 0x00, 40, 200, 255, 128, 0 };
        Assert.True(VirtualDs4.TryParseOutputReport(report, out var f));
        Assert.Equal(200, f.HeavyMotor);
        Assert.Equal(40, f.LightMotor);
        Assert.Equal((byte)255, f.LightbarRed);
        Assert.Equal((byte)128, f.LightbarGreen);
        Assert.Equal((byte)0, f.LightbarBlue);
    }

    [Fact]
    public void Game_output_report_without_flags_is_ignored()
    {
        byte[] report = { 0x05, 0x00, 0x00, 0x00, 40, 200, 255, 128, 0 };
        Assert.False(VirtualDs4.TryParseOutputReport(report, out _));
    }
}

public class XboxAndHidHideTests
{
    [Theory]
    [InlineData(128, false, 0)]
    [InlineData(255, false, 32767)]
    [InlineData(0, false, -32768)]
    [InlineData(0, true, 32767)]      // stick en haut → Y positif côté Xbox
    [InlineData(255, true, -32768)]
    public void Axis_conversion(byte input, bool invert, short expected)
    {
        Assert.Equal(expected, VirtualXbox360.ToXboxAxis(input, invert));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6&mi_03#7&519de54&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
                @"HID\VID_054C&PID_0CE6&MI_03\7&519DE54&0&0000")]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&abc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
                @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\9&ABC&0&0000")]
    public void Device_path_is_converted_to_instance_id(string path, string expected)
    {
        Assert.Equal(expected, HidHideService.ToInstanceId(path));
    }
}
