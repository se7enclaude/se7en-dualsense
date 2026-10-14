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
/// Vibrations générées depuis le son de Windows (capture « loopback » de la sortie audio) :
/// les basses font vibrer le gros moteur, les médiums le petit. Utile pour les jeux sans vibrations.
/// </summary>
public sealed class AudioRumbleService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly Timer _silence;
    private DateTime _lastData = DateTime.MinValue;
    private double _envLow, _envMid;
    private Biquad _lowL, _lowR, _bandHp, _bandLp;
    private AudioRumbleMode _mode = AudioRumbleMode.Off;
    private double _intensity = 1;
    private int _sampleRate;

    /// <summary>(gros moteur, petit moteur) en 0-255, environ 100 fois par seconde (thread audio).</summary>
    public event Action<byte, byte>? Levels;

    public AudioRumbleService()
    {
        // Sans son, la capture ne livre plus rien : on retombe à zéro
        _silence = new Timer(_ =>
        {
            if (_capture != null && DateTime.UtcNow - _lastData > TimeSpan.FromMilliseconds(120))
            {
                _envLow = _envMid = 0;
                Levels?.Invoke(0, 0);
            }
        }, null, 200, 100);
    }

    public bool IsRunning => _capture != null;

    public void Configure(AudioRumbleMode mode, double intensity)
    {
        _intensity = Math.Clamp(intensity, 0, 2);
        if (mode == _mode) return;
        _mode = mode;
        if (mode == AudioRumbleMode.Off) Stop(); else Start();
    }

    private void Start()
    {
        if (_capture != null) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _lowL = Biquad.LowPass(_sampleRate, 140);
            _lowR = Biquad.LowPass(_sampleRate, 140);
            _bandHp = Biquad.HighPass(_sampleRate, 250);
            _bandLp = Biquad.LowPass(_sampleRate, 2500);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, _) => { };
            _capture.StartRecording();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c == null) return;
        try
        {
            c.DataAvailable -= OnData;
            c.StopRecording();
            c.Dispose();
        }
        catch { /* périphérique déjà parti */ }
        Levels?.Invoke(0, 0);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format == null || format.Encoding != WaveFormatEncoding.IeeeFloat && format.Encoding != WaveFormatEncoding.Extensible) return;
        if (format.BitsPerSample != 32 || e.BytesRecorded == 0) return;
        _lastData = DateTime.UtcNow;

        int channels = format.Channels;
        int frames = e.BytesRecorded / 4 / channels;
        double sumLow = 0, sumMid = 0;
        var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
        for (int i = 0; i < frames; i++)
        {
            double l = span[i * channels];
            double r = channels > 1 ? span[i * channels + 1] : l;
            double mono = (l + r) * 0.5;
            double low = (_lowL.Process(l) + _lowR.Process(r)) * 0.5;
            double mid = _bandLp.Process(_bandHp.Process(mono));
            sumLow += low * low;
            sumMid += mid * mid;
        }
        double rmsLow = Math.Sqrt(sumLow / Math.Max(1, frames));
        double rmsMid = Math.Sqrt(sumMid / Math.Max(1, frames));

        // Enveloppe : attaque rapide, relâchement doux ; seuil pour ignorer le bruit de fond
        double dt = frames / (double)_sampleRate;
        Follow(ref _envLow, Shape(rmsLow, 5.5), dt);
        Follow(ref _envMid, _mode == AudioRumbleMode.Full ? Shape(rmsMid, 4) : 0, dt);
        Levels?.Invoke((byte)Math.Clamp(_envLow * 255 * _intensity, 0, 255), (byte)Math.Clamp(_envMid * 200 * _intensity, 0, 255));
    }

    private static double Shape(double rms, double gain)
    {
        double v = rms * gain - 0.04;
        return v <= 0 ? 0 : Math.Min(1, Math.Pow(v, 0.8));
    }

    private static void Follow(ref double env, double target, double dt)
    {
        double tau = target > env ? 0.012 : 0.14;
        env += (target - env) * (1 - Math.Exp(-dt / tau));
    }

    public void Dispose()
    {
        _silence.Dispose();
        Stop();
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
