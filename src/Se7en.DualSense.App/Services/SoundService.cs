using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Se7en.DualSense.App.Services;

public enum UiSound
{
    Click,
    Tick,
    ToggleOn,
    ToggleOff,
    Connect,
    Disconnect,
    Notify,
    Success,
    Warning,
    Intro,
    RecordStart,
    RecordStop,
}

/// <summary>
/// Effets sonores de l'interface, synthétisés au démarrage (aucun fichier audio) : clics, bascules,
/// carillons de connexion, notifications, intro. Un seul flux de sortie, ouvert à la demande et refermé
/// après quelques secondes de silence.
/// </summary>
public static class SoundService
{
    private const int Rate = 44100;
    private static readonly Dictionary<UiSound, float[]> Bank = new();
    private static readonly object Lock = new();
    private static WasapiOut? _out;
    private static MixingSampleProvider? _mixer;
    private static DateTime _lastPlay;
    private static Timer? _idle;
    private static DateTime _lastTick;

    public static bool Enabled => AppSettings.Current.Sounds;

    /// <summary>Branche les sons sur tous les boutons, bascules et onglets de l'application.</summary>
    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((s, _) =>
        {
            switch (s)
            {
                case ToggleButton { Style: { } st } t when t is not RadioButton && Equals(st, Application.Current.TryFindResource("Switch")):
                    Play(t.IsChecked == true ? UiSound.ToggleOn : UiSound.ToggleOff);
                    break;
                case RadioButton:
                    Play(UiSound.Tick);
                    break;
                case CheckBox c:
                    Play(c.IsChecked == true ? UiSound.ToggleOn : UiSound.ToggleOff);
                    break;
                default:
                    Play(UiSound.Click);
                    break;
            }
        }), handledEventsToo: true);
    }

    /// <summary>Petit « tic » pour la navigation à la manette (limité pour ne pas saturer).</summary>
    public static void Tick()
    {
        if ((DateTime.UtcNow - _lastTick).TotalMilliseconds < 45) return;
        _lastTick = DateTime.UtcNow;
        Play(UiSound.Tick);
    }

    public static void Play(UiSound sound)
    {
        if (!Enabled) return;
        try
        {
            lock (Lock)
            {
                if (!Bank.TryGetValue(sound, out var samples))
                    Bank[sound] = samples = Synthesize(sound);
                EnsureOutput();
                float volume = (float)Math.Clamp(AppSettings.Current.SoundVolume, 0, 1);
                var provider = new VolumeSampleProvider(new BufferProvider(samples)) { Volume = volume };
                _mixer!.AddMixerInput(provider);
                _lastPlay = DateTime.UtcNow;
            }
        }
        catch
        {
            // Pas de sortie audio : silence
        }
    }

    private static void EnsureOutput()
    {
        if (_out != null) return;
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)) { ReadFully = true };
        _out = new WasapiOut(AudioClientShareMode.Shared, 50);
        _out.Init(new SampleToWaveProvider(_mixer));
        _out.Play();
        _idle ??= new Timer(_ =>
        {
            lock (Lock)
            {
                if (_out == null || DateTime.UtcNow - _lastPlay < TimeSpan.FromSeconds(4)) return;
                _out.Stop();
                _out.Dispose();
                _out = null;
                _mixer = null;
            }
        }, null, 2000, 2000);
    }

    // ───────────── Synthèse ─────────────

    private static float[] Synthesize(UiSound sound) => sound switch
    {
        UiSound.Click => Mix(Tone(1900, 0.018, 0.25, decay: 260), Noise(0.006, 0.08)),
        UiSound.Tick => Tone(2600, 0.012, 0.14, decay: 400),
        UiSound.ToggleOn => Concat(Tone(880, 0.045, 0.22, decay: 60), Tone(1320, 0.07, 0.22, decay: 45)),
        UiSound.ToggleOff => Concat(Tone(1100, 0.045, 0.2, decay: 60), Tone(740, 0.07, 0.2, decay: 45)),
        UiSound.Connect => Concat(Bell(784, 0.09, 0.3), Bell(1046.5, 0.09, 0.3), Bell(1568, 0.35, 0.28)),
        UiSound.Disconnect => Concat(Bell(1046.5, 0.09, 0.26), Bell(659, 0.3, 0.26)),
        UiSound.Notify => Bell(1318.5, 0.32, 0.22),
        UiSound.Success => Concat(Bell(1046.5, 0.08, 0.24), Bell(1568, 0.3, 0.24)),
        UiSound.Warning => Concat(Tone(520, 0.11, 0.26, decay: 18), Silence(0.05), Tone(520, 0.16, 0.26, decay: 14)),
        UiSound.RecordStart => Concat(Tone(660, 0.06, 0.24, decay: 30), Tone(990, 0.12, 0.24, decay: 22)),
        UiSound.RecordStop => Concat(Tone(990, 0.06, 0.24, decay: 30), Tone(660, 0.12, 0.24, decay: 22)),
        UiSound.Intro => Intro(),
        _ => Tone(1000, 0.02, 0.2),
    };

    /// <summary>Intro : souffle qui monte puis accord lumineux.</summary>
    private static float[] Intro()
    {
        int n = (int)(Rate * 1.6);
        var s = new float[n];
        var rnd = new Random(7);
        double lp = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            // Souffle filtré qui s'ouvre (0 → 0,7 s)
            double cutoff = Math.Clamp(t / 0.7, 0, 1);
            lp += (rnd.NextDouble() * 2 - 1 - lp) * (0.02 + 0.25 * cutoff);
            double whoosh = lp * Math.Sin(Math.PI * Math.Min(t / 0.8, 1)) * 0.35;
            // Accord (do – sol – do – mi) à partir de 0,62 s
            double chord = 0;
            if (t > 0.62)
            {
                double u = t - 0.62;
                double env = Math.Min(u / 0.01, 1) * Math.Exp(-u * 2.6);
                foreach (var f in new[] { 523.25, 783.99, 1046.5, 1318.5 })
                    chord += (Math.Sin(2 * Math.PI * f * u) + 0.3 * Math.Sin(4 * Math.PI * f * u)) * env * 0.085;
            }
            s[i] = (float)(whoosh + chord);
        }
        return s;
    }

    private static float[] Tone(double freq, double seconds, double amp, double decay = 25)
    {
        int n = (int)(Rate * seconds);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Math.Min(t / 0.003, 1) * Math.Exp(-t * decay) * Math.Min((seconds - t) / 0.004, 1);
            s[i] = (float)(Math.Sin(2 * Math.PI * freq * t) * amp * env);
        }
        return s;
    }

    /// <summary>Son de cloche douce (fondamentale + partiels).</summary>
    private static float[] Bell(double freq, double seconds, double amp)
    {
        int n = (int)(Rate * seconds);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double env = Math.Min(t / 0.004, 1) * Math.Exp(-t * 9) * Math.Min((seconds - t) / 0.01, 1);
            double v = Math.Sin(2 * Math.PI * freq * t) + 0.35 * Math.Sin(2 * Math.PI * freq * 2.01 * t) * Math.Exp(-t * 14) + 0.12 * Math.Sin(2 * Math.PI * freq * 3.02 * t) * Math.Exp(-t * 20);
            s[i] = (float)(v * amp * env * 0.7);
        }
        return s;
    }

    private static float[] Noise(double seconds, double amp)
    {
        var rnd = new Random(3);
        int n = (int)(Rate * seconds);
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = (float)((rnd.NextDouble() * 2 - 1) * amp * (1 - (double)i / n));
        return s;
    }

    private static float[] Silence(double seconds) => new float[(int)(Rate * seconds)];

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static float[] Mix(float[] a, float[] b)
    {
        var s = new float[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < s.Length; i++)
            s[i] = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
        return s;
    }

    private sealed class BufferProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _pos;

        public BufferProvider(float[] samples) => _samples = samples;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _samples.Length - _pos);
            Array.Copy(_samples, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }
}
