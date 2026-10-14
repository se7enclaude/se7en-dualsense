using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Se7en.DualSense.Output;

namespace Se7en.DualSense.App.Services;

/// <summary>Micro de Windows (périphérique d'enregistrement par défaut) : état et bascule couper / rétablir.</summary>
public static class MicrophoneService
{
    /// <summary>Vrai si le micro par défaut est coupé ; null s'il n'y a pas de micro.</summary>
    public static bool? IsMuted()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return null;
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return device.AudioEndpointVolume.Mute;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Coupe ou rétablit le micro (par défaut et communications) ; renvoie le nouvel état.</summary>
    public static bool? Toggle()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return null;
            using var comms = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            bool mute = !comms.AudioEndpointVolume.Mute;
            comms.AudioEndpointVolume.Mute = mute;
            using var console = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            if (console.ID != comms.ID)
                console.AudioEndpointVolume.Mute = mute;
            return mute;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Périphériques audio de la DualSense (carte son USB : haut-parleur / prise casque à 4 canaux, micro)
/// et choix des périphériques par défaut de Windows.
/// </summary>
public static class AudioDevices
{
    private static bool IsController(MMDevice d)
    {
        try
        {
            string name = d.FriendlyName + " " + d.DeviceFriendlyName;
            return name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("DualSense", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sortie audio de la manette (USB uniquement), ou null.</summary>
    public static MMDevice? ControllerOutput(MMDeviceEnumerator e) =>
        e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(IsController);

    public static MMDevice? ControllerMicrophone(MMDeviceEnumerator e) =>
        e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).FirstOrDefault(IsController);

    public static string? DefaultId(DataFlow flow)
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            if (!e.HasDefaultAudioEndpoint(flow, Role.Multimedia)) return null;
            using var d = e.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return d.ID;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Statut de la manette côté son : sortie / micro présents, et sont-ils les périphériques par défaut.</summary>
    public static (string? OutputId, string? MicId) Controller()
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            return (ControllerOutput(e)?.ID, ControllerMicrophone(e)?.ID);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Définit le périphérique par défaut de Windows (tous les rôles).</summary>
    public static bool SetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            for (int role = 0; role < 3; role++)
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Interface non documentée mais stable depuis Windows 7, utilisée par tous les outils de bascule audio
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }
}

/// <summary>Ce que le moteur audio doit produire (calculé à partir du profil et des réglages).</summary>
public sealed record AudioEngineConfig
{
    public AudioRumbleMode Rumble { get; init; }
    public double Intensity { get; init; } = 1;
    /// <summary>Niveaux sonores pour la barre lumineuse « musique ».</summary>
    public bool Levels { get; init; }
    /// <summary>Détection des sons forts pour les gâchettes.</summary>
    public bool Transients { get; init; }
    /// <summary>Copie du son du PC dans le haut-parleur / casque de la manette.</summary>
    public bool Mirror { get; init; }
    public double MirrorVolume { get; init; } = 0.8;

    public bool NeedsCapture => Rumble != AudioRumbleMode.Off || Levels || Transients || Mirror;
    public bool NeedsControllerOutput => Rumble == AudioRumbleMode.Haptics || Mirror;
}

/// <summary>
/// Moteur audio : capture le son de Windows (loopback) et en tire
///  - les vibrations classiques (basses → gros moteur, médiums → petit moteur) ;
///  - l'haptique HD : signal envoyé aux canaux 3-4 de la carte son USB de la DualSense (actionneurs) ;
///  - la copie du son dans le haut-parleur / casque de la manette (canaux 1-2) ;
///  - le niveau sonore (barre lumineuse) et les sons forts (gâchettes).
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly object _lock = new();
    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _out;
    private HapticProvider? _provider;
    private readonly Timer _silence;
    private DateTime _lastData = DateTime.MinValue;
    private AudioEngineConfig _config = new();
    private double _envLow, _envMid, _envFast, _envSlow, _level;
    private Biquad _lowL, _lowR, _bandHp, _bandLp;
    private int _sampleRate;
    private bool _loopsBack;

    /// <summary>(gros moteur, petit moteur) en 0-255 (modes Basses / Basses + médiums).</summary>
    public event Action<byte, byte>? Rumble;
    /// <summary>Son fort et soudain (tir, explosion).</summary>
    public event Action? Transient;

    /// <summary>Niveau sonore global lissé (0-1).</summary>
    public double Level => _level;

    /// <summary>Vrai si la sortie haptique / haut-parleur de la manette est active.</summary>
    public bool ControllerOutputActive => _out != null;

    public AudioEngine()
    {
        // Sans son, la capture ne livre plus rien : on retombe à zéro
        _silence = new Timer(_ =>
        {
            if (_capture != null && DateTime.UtcNow - _lastData > TimeSpan.FromMilliseconds(120))
            {
                _envLow = _envMid = _level = 0;
                Rumble?.Invoke(0, 0);
            }
        }, null, 200, 100);
    }

    public void Configure(AudioEngineConfig config)
    {
        lock (_lock)
        {
            var old = _config;
            _config = config;
            if (_provider != null)
            {
                _provider.Haptics = config.Rumble == AudioRumbleMode.Haptics;
                _provider.Intensity = config.Intensity;
                _provider.MirrorVolume = config.Mirror && !_loopsBack ? config.MirrorVolume : 0;
            }
            if (old == config && (_capture != null) == config.NeedsCapture && (_out != null) == config.NeedsControllerOutput) return;
            Restart();
        }
    }

    /// <summary>Vibrations demandées par le jeu, reproduites par l'haptique HD.</summary>
    public void SetGameRumble(byte heavy, byte light)
    {
        if (_provider != null) _provider.SetRumble(heavy, light);
    }

    /// <summary>Relance après un changement de périphérique (manette branchée en USB, sortie par défaut changée).</summary>
    public void Refresh()
    {
        lock (_lock) Restart();
    }

    private void Restart()
    {
        StopAll();
        var c = _config;
        if (c.NeedsControllerOutput) StartControllerOutput(c);
        if (c.NeedsCapture) StartCapture();
    }

    private void StartControllerOutput(AudioEngineConfig c)
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            var device = AudioDevices.ControllerOutput(e);
            if (device == null) return;
            // Si la manette est déjà la sortie de Windows, la capturer créerait une boucle : pas de copie du son
            _loopsBack = AudioDevices.DefaultId(DataFlow.Render) == device.ID;
            var format = device.AudioClient.MixFormat;
            _provider = new HapticProvider(format)
            {
                Haptics = c.Rumble == AudioRumbleMode.Haptics,
                Intensity = c.Intensity,
                MirrorVolume = c.Mirror && !_loopsBack ? c.MirrorVolume : 0,
            };
            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
            _out.Init(_provider);
            _out.Play();
        }
        catch
        {
            _out?.Dispose();
            _out = null;
            _provider = null;
        }
    }

    private void StartCapture()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _lowL = Biquad.LowPass(_sampleRate, 140);
            _lowR = Biquad.LowPass(_sampleRate, 140);
            _bandHp = Biquad.HighPass(_sampleRate, 250);
            _bandLp = Biquad.LowPass(_sampleRate, 2500);
            _provider?.SetInputRate(_sampleRate);
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void StopAll()
    {
        var c = _capture;
        _capture = null;
        if (c != null)
        {
            try
            {
                c.DataAvailable -= OnData;
                c.StopRecording();
                c.Dispose();
            }
            catch { /* périphérique déjà parti */ }
        }
        var o = _out;
        _out = null;
        _provider = null;
        if (o != null)
        {
            try { o.Stop(); o.Dispose(); } catch { /* idem */ }
        }
        Rumble?.Invoke(0, 0);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format == null || format.BitsPerSample != 32 || e.BytesRecorded == 0) return;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat && format.Encoding != WaveFormatEncoding.Extensible) return;
        _lastData = DateTime.UtcNow;
        var cfg = _config;

        int channels = format.Channels;
        int frames = e.BytesRecorded / 4 / channels;
        var span = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
        _provider?.Push(span, channels, frames);

        double sumLow = 0, sumMid = 0, sumAll = 0;
        for (int i = 0; i < frames; i++)
        {
            double l = span[i * channels];
            double r = channels > 1 ? span[i * channels + 1] : l;
            double mono = (l + r) * 0.5;
            double low = (_lowL.Process(l) + _lowR.Process(r)) * 0.5;
            double mid = _bandLp.Process(_bandHp.Process(mono));
            sumLow += low * low;
            sumMid += mid * mid;
            sumAll += mono * mono;
        }
        double n = Math.Max(1, frames);
        double rmsLow = Math.Sqrt(sumLow / n), rmsMid = Math.Sqrt(sumMid / n), rmsAll = Math.Sqrt(sumAll / n);
        double dt = frames / (double)_sampleRate;

        // Enveloppes : attaque rapide, relâchement doux ; seuil pour ignorer le bruit de fond
        Follow(ref _envLow, Shape(rmsLow, 5.5), dt, 0.012, 0.14);
        Follow(ref _envMid, cfg.Rumble == AudioRumbleMode.Full ? Shape(rmsMid, 4) : 0, dt, 0.012, 0.14);
        Follow(ref _level, Math.Min(1, rmsAll * 4), dt, 0.02, 0.25);

        if (cfg.Rumble is AudioRumbleMode.Bass or AudioRumbleMode.Full)
            Rumble?.Invoke((byte)Math.Clamp(_envLow * 255 * cfg.Intensity, 0, 255), (byte)Math.Clamp(_envMid * 200 * cfg.Intensity, 0, 255));

        if (cfg.Transients)
        {
            // Son fort et soudain : l'enveloppe rapide dépasse nettement la lente
            double prevFast = _envFast;
            Follow(ref _envFast, rmsAll, dt, 0.004, 0.05);
            Follow(ref _envSlow, rmsAll, dt, 0.3, 0.3);
            if (_envFast > 0.12 && _envFast > _envSlow * 2.2 && prevFast <= _envSlow * 2.2)
                Transient?.Invoke();
        }
    }

    private static double Shape(double rms, double gain)
    {
        double v = rms * gain - 0.04;
        return v <= 0 ? 0 : Math.Min(1, Math.Pow(v, 0.8));
    }

    private static void Follow(ref double env, double target, double dt, double attack, double release)
    {
        double tau = target > env ? attack : release;
        env += (target - env) * (1 - Math.Exp(-dt / tau));
    }

    public void Dispose()
    {
        _silence.Dispose();
        lock (_lock) StopAll();
    }

    /// <summary>
    /// Flux envoyé à la carte son de la manette : canaux 1-2 = copie du son du PC, canaux 3-4 = actionneurs haptiques
    /// (basses du son + vibrations du jeu synthétisées).
    /// </summary>
    private sealed class HapticProvider : IWaveProvider
    {
        private readonly float[] _ring;
        private long _write, _read;
        private readonly object _sync = new();
        private double _ratio = 1, _frac;
        private Biquad _hapticLp;
        private double _phaseHeavy, _phaseLight, _heavy, _light, _heavyTarget, _lightTarget;
        private readonly int _channels, _rate;

        public HapticProvider(WaveFormat deviceFormat)
        {
            _channels = deviceFormat.Channels;
            _rate = deviceFormat.SampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_rate, _channels);
            if (deviceFormat is WaveFormatExtensible) WaveFormat = deviceFormat;
            _ring = new float[_rate * 2]; // 1 s stéréo
            _hapticLp = Biquad.LowPass(_rate, 300);
        }

        public WaveFormat WaveFormat { get; }
        public bool Haptics { get; set; }
        public double Intensity { get; set; } = 1;
        public double MirrorVolume { get; set; }

        public void SetInputRate(int rate) => _ratio = rate / (double)_rate;

        public void SetRumble(byte heavy, byte light)
        {
            _heavyTarget = heavy / 255.0;
            _lightTarget = light / 255.0;
        }

        public void Push(ReadOnlySpan<float> samples, int channels, int frames)
        {
            lock (_sync)
            {
                for (int i = 0; i < frames; i++)
                {
                    _ring[_write++ % _ring.Length] = samples[i * channels];
                    _ring[_write++ % _ring.Length] = channels > 1 ? samples[i * channels + 1] : samples[i * channels];
                }
                // Latence plafonnée à ~80 ms : on saute ce qui est en retard
                long max = (long)(_rate * 0.08 * _ratio) * 2;
                if (_write - _read > max) _read = _write - max;
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
            int frames = output.Length / _channels;
            lock (_sync)
            {
                for (int f = 0; f < frames; f++)
                {
                    float l = 0, r = 0;
                    if (_write - _read >= 4)
                    {
                        // Rééchantillonnage linéaire (fréquence de capture → fréquence de la manette)
                        long i0 = _read;
                        float l0 = _ring[i0 % _ring.Length], r0 = _ring[(i0 + 1) % _ring.Length];
                        float l1 = _ring[(i0 + 2) % _ring.Length], r1 = _ring[(i0 + 3) % _ring.Length];
                        l = (float)(l0 + (l1 - l0) * _frac);
                        r = (float)(r0 + (r1 - r0) * _frac);
                        _frac += _ratio;
                        while (_frac >= 1) { _frac -= 1; _read += 2; }
                    }

                    int b = f * _channels;
                    output[b] = (float)(l * MirrorVolume);
                    if (_channels > 1) output[b + 1] = (float)(r * MirrorVolume);
                    for (int c = 2; c < _channels; c++) output[b + c] = 0;

                    if (Haptics && _channels >= 4)
                    {
                        _heavy += (_heavyTarget - _heavy) * 0.002;
                        _light += (_lightTarget - _light) * 0.002;
                        _phaseHeavy += 2 * Math.PI * 60 / _rate;
                        _phaseLight += 2 * Math.PI * 170 / _rate;
                        double synth = _heavy * Math.Sin(_phaseHeavy) * 0.8 + _light * Math.Sin(_phaseLight) * 0.5;
                        double bass = _hapticLp.Process((l + r) * 0.5) * 2.5;
                        float h = (float)Math.Clamp((bass + synth) * Intensity, -1, 1);
                        output[b + 2] = h;
                        output[b + 3] = h;
                    }
                }
                if (_phaseHeavy > 1e6) _phaseHeavy %= 2 * Math.PI;
                if (_phaseLight > 1e6) _phaseLight %= 2 * Math.PI;
            }
            return count;
        }
    }

    /// <summary>Filtre biquadratique (formules RBJ).</summary>
    private struct Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2, _z1, _z2;

        public static Biquad LowPass(double fs, double f) => Make(fs, f, low: true);
        public static Biquad HighPass(double fs, double f) => Make(fs, f, low: false);

        private static Biquad Make(double fs, double f, bool low)
        {
            double w = 2 * Math.PI * f / fs, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.7071);
            double a0 = 1 + alpha;
            double b0 = low ? (1 - cos) / 2 : (1 + cos) / 2;
            double b1 = low ? 1 - cos : -(1 + cos);
            return new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b0 / a0, _a1 = -2 * cos / a0, _a2 = (1 - alpha) / a0 };
        }

        public double Process(double x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }
    }
}
