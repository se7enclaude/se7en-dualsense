using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Se7en.DualSense.App.Services;

/// <summary>
/// Association Bluetooth de la DualSense depuis l'application (API Windows) : recherche d'une manette
/// en mode appairage (PS + Create maintenus), puis association sans passer par les Paramètres.
/// </summary>
public static class BluetoothPairing
{
    private static bool IsDualSense(string? name) =>
        name != null && (name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("DualSense", StringComparison.OrdinalIgnoreCase));

    /// <summary>Cherche une manette à associer pendant <paramref name="timeout"/>, puis l'associe.</summary>
    public static async Task<string> PairAsync(IProgress<string> progress, TimeSpan timeout, CancellationToken ct)
    {
        progress.Report("Recherche d'une DualSense en mode association…");
        var found = new TaskCompletionSource<DeviceInformation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
        watcher.Added += (_, info) => { if (IsDualSense(info.Name)) found.TrySetResult(info); };
        watcher.Updated += (_, _) => { };
        watcher.Start();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await using (cts.Token.Register(() => found.TrySetCanceled()))
            {
                DeviceInformation device;
                try
                {
                    device = await found.Task;
                }
                catch (TaskCanceledException)
                {
                    return ct.IsCancellationRequested
                        ? "Recherche annulée."
                        : "Aucune manette trouvée. Maintenez PS + Create jusqu'à ce que la barre lumineuse clignote rapidement, puis réessayez.";
                }

                progress.Report($"« {device.Name} » trouvée, association…");
                if (device.Pairing.IsPaired) return "Cette manette est déjà associée : appuyez sur PS pour la connecter.";
                var custom = device.Pairing.Custom;
                custom.PairingRequested += (_, args) => args.Accept();
                var result = await custom.PairAsync(
                    DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin | DevicePairingKinds.ConfirmPinMatch,
                    DevicePairingProtectionLevel.None);
                return result.Status switch
                {
                    DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired => "Manette associée ! Elle se connecte toute seule ; ensuite, PS suffit pour l'allumer.",
                    _ => $"Association refusée par Windows ({result.Status}). Essayez depuis Paramètres › Bluetooth.",
                };
            }
        }
        finally
        {
            try { watcher.Stop(); } catch { /* déjà arrêté */ }
        }
    }
}
