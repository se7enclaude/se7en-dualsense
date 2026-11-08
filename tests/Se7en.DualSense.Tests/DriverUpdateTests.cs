using Se7en.DualSense.Output;
using Xunit;

namespace Se7en.DualSense.Tests;

public class DriverUpdateTests
{
    [Theory]
    [InlineData("v1.5.230.0", "1.5.230")]
    [InlineData("1.5.230", "1.5.230.0")]
    [InlineData("v1.22.0", "1.22")]
    [InlineData("1.21.442.0", "v1.21.442")]
    public void Equivalent_versions_are_equal(string a, string b) =>
        Assert.Equal(DriverVersion.Parse(a), DriverVersion.Parse(b));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void Unreadable_versions_are_null(string? text) => Assert.Null(DriverVersion.Parse(text));

    [Theory]
    [InlineData("v1.5.230.0", "1.5.230")]
    [InlineData("v1.22.0", "1.22.0")]
    [InlineData("1.6.1.4", "1.6.1.4")]
    public void Display_drops_a_null_fourth_part(string tag, string expected) => Assert.Equal(expected, DriverVersion.Display(tag));

    [Theory]
    [InlineData(true, "1.5.230", "v1.5.230.0", DriverUpdateState.UpToDate)]
    [InlineData(true, "1.22.0", "v1.22.0", DriverUpdateState.UpToDate)]
    [InlineData(true, "1.23.0", "v1.22.0", DriverUpdateState.UpToDate)]      // plus récent que la publication : pas de « mise à jour » vers l'arrière
    [InlineData(true, "1.5.230", "v1.5.231.0", DriverUpdateState.UpdateAvailable)]
    [InlineData(true, "1.9.0", "v1.10.0", DriverUpdateState.UpdateAvailable)] // comparaison numérique, pas alphabétique
    [InlineData(true, null, "v1.22.0", DriverUpdateState.InstalledVersionUnknown)]
    [InlineData(true, "1.22.0", null, DriverUpdateState.NotChecked)]
    [InlineData(false, null, "v1.22.0", DriverUpdateState.NotInstalled)]
    public void Compare_installed_with_latest(bool installed, string? current, string? latest, DriverUpdateState expected) =>
        Assert.Equal(expected, DriverUpdateLogic.Compare(installed, current, latest));

    [Fact]
    public void Check_is_due_at_most_every_12_hours()
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(DriverUpdateLogic.IsCheckDue(null, now));
        Assert.False(DriverUpdateLogic.IsCheckDue(now.AddHours(-11.9), now));
        Assert.True(DriverUpdateLogic.IsCheckDue(now.AddHours(-12), now));
        Assert.True(DriverUpdateLogic.IsCheckDue(now.AddDays(3), now)); // horloge reculée : on revérifie
    }

    [Fact]
    public void Notify_only_once_per_version()
    {
        Assert.True(DriverUpdateLogic.ShouldNotify(DriverUpdateState.UpdateAvailable, "v1.6.0", null));
        Assert.False(DriverUpdateLogic.ShouldNotify(DriverUpdateState.UpdateAvailable, "v1.6.0", "v1.6.0"));
        Assert.False(DriverUpdateLogic.ShouldNotify(DriverUpdateState.UpdateAvailable, "v1.6.0.0", "1.6.0"));
        Assert.True(DriverUpdateLogic.ShouldNotify(DriverUpdateState.UpdateAvailable, "v1.7.0", "v1.6.0"));
        Assert.False(DriverUpdateLogic.ShouldNotify(DriverUpdateState.UpToDate, "v1.6.0", null));
    }

    [Fact]
    public void Texts_are_in_french()
    {
        var checkedOn = new DateTime(2026, 11, 9, 14, 5, 0);
        Assert.Equal("9 nov. 2026", DriverUpdateLogic.FormatDate(checkedOn));
        Assert.Equal("1 août 2026", DriverUpdateLogic.FormatDate(new DateTime(2026, 8, 1)));
        Assert.Equal("le 9 nov. 2026 à 14:05", DriverUpdateLogic.FormatDateTime(checkedOn));
        Assert.Equal("À jour (version 1.22.0, vérifié le 9 nov. 2026)",
            DriverUpdateLogic.Describe(DriverUpdateState.UpToDate, "1.22.0", "v1.22.0", checkedOn));
        Assert.Equal("Mise à jour disponible : 1.5.230 → 1.6.0",
            DriverUpdateLogic.Describe(DriverUpdateState.UpdateAvailable, "1.5.230", "v1.6.0.0", checkedOn));
    }

    [Fact]
    public void State_file_round_trips_and_survives_corruption()
    {
        string dir = Path.Combine(Path.GetTempPath(), "se7en-driver-state-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "driver-updates.json");
        try
        {
            var state = new DriverUpdateStateFile { LastCheckUtc = new DateTime(2026, 11, 9, 13, 5, 0, DateTimeKind.Utc) };
            state.Latest["HidHide"] = new DriverReleaseInfo { Tag = "v1.5.230.0", AssetName = "HidHide_1.5.230_x64.exe", AssetUrl = "https://github.com/x", Sha256 = new string('A', 64) };
            state.Notified["HidHide"] = "v1.5.230.0";
            state.Save(path);

            var back = DriverUpdateStateFile.Load(path);
            Assert.Equal(state.LastCheckUtc, back.LastCheckUtc);
            Assert.Equal("v1.5.230.0", back.LatestOf(DriverKind.HidHide)!.Tag);
            Assert.Equal(new string('A', 64), back.LatestOf(DriverKind.HidHide)!.Sha256);
            Assert.Null(back.LatestOf(DriverKind.ViGEmBus));
            Assert.Equal("v1.5.230.0", back.Notified["HidHide"]);

            File.WriteAllText(path, "{ pas du json");
            var broken = DriverUpdateStateFile.Load(path);
            Assert.Null(broken.LastCheckUtc);
            Assert.Empty(broken.Latest);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Release_json_with_digest_gives_sha256()
    {
        const string json = """
        { "tag_name": "v1.6.0.0", "assets": [
          { "name": "HidHide_1.6.0_arm64.exe", "browser_download_url": "https://github.com/a", "size": 1, "digest": null },
          { "name": "HidHide_1.6.0_x64.exe", "browser_download_url": "https://github.com/nefarius/HidHide/releases/download/v1.6.0.0/HidHide_1.6.0_x64.exe",
            "size": 8078016, "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" } ] }
        """;
        var r = DriverInstaller.ParseRelease(DriverKind.HidHide, json);
        Assert.Equal("v1.6.0.0", r.Tag);
        Assert.Equal("HidHide_1.6.0_x64.exe", r.AssetName);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", r.Sha256);
        Assert.Equal(8078016, r.Size);
    }

    [Fact]
    public void Release_json_without_digest_has_no_sha256()
    {
        const string json = """
        { "tag_name": "v1.22.0", "assets": [
          { "name": "ViGEmBus_1.22.0_x64_x86_arm64.exe", "browser_download_url": "https://github.com/b", "size": 6278576, "digest": null } ] }
        """;
        var r = DriverInstaller.ParseRelease(DriverKind.ViGEmBus, json);
        Assert.Null(r.Sha256);
        Assert.Equal(new Version(1, 22, 0, 0), r.Version);
        Assert.Throws<InvalidOperationException>(() => DriverInstaller.ParseRelease(DriverKind.ViGEmBus, """{ "tag_name": "v1", "assets": [] }"""));
    }
}
