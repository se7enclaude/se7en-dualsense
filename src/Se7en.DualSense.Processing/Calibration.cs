using Se7en.DualSense.Hid;

namespace Se7en.DualSense.Processing;

/// <summary>
/// Assistant de calibration, en deux temps :
///  1. manette posée : moyenne de la position de repos de chaque stick (le centre réel) ;
///  2. rotation des sticks : relevé des extrêmes sur chaque axe (l'amplitude réelle).
/// Alimenté depuis le thread d'E/S, lu depuis l'interface (accès protégés par un verrou léger).
/// </summary>
public sealed class CalibrationSampler
{
    private readonly object _lock = new();
    private double _sumLX, _sumLY, _sumRX, _sumRY;
    private double _sqLX, _sqLY, _sqRX, _sqRY;
    private int _count;
    private double _minLX = 1, _maxLX = -1, _minLY = 1, _maxLY = -1;
    private double _minRX = 1, _maxRX = -1, _minRY = 1, _maxRY = -1;

    public enum Stage { Idle, Center, Range }

    public Stage Current { get; private set; } = Stage.Idle;

    public int SampleCount { get { lock (_lock) return _count; } }

    public void BeginCenter()
    {
        lock (_lock)
        {
            _sumLX = _sumLY = _sumRX = _sumRY = 0;
            _sqLX = _sqLY = _sqRX = _sqRY = 0;
            _count = 0;
            Current = Stage.Center;
        }
    }

    public void BeginRange()
    {
        lock (_lock)
        {
            _minLX = _minLY = _minRX = _minRY = 1;
            _maxLX = _maxLY = _maxRX = _maxRY = -1;
            Current = Stage.Range;
        }
    }

    public void Stop() { lock (_lock) Current = Stage.Idle; }

    /// <summary>À appeler à chaque rapport brut.</summary>
    public void Add(in DualSenseState s)
    {
        if (Current == Stage.Idle) return;
        double lx = StickMath.Normalize(s.LeftX), ly = StickMath.Normalize(s.LeftY);
        double rx = StickMath.Normalize(s.RightX), ry = StickMath.Normalize(s.RightY);
        lock (_lock)
        {
            if (Current == Stage.Center)
            {
                _sumLX += lx; _sumLY += ly; _sumRX += rx; _sumRY += ry;
                _sqLX += lx * lx; _sqLY += ly * ly; _sqRX += rx * rx; _sqRY += ry * ry;
                _count++;
            }
            else if (Current == Stage.Range)
            {
                _minLX = Math.Min(_minLX, lx); _maxLX = Math.Max(_maxLX, lx);
                _minLY = Math.Min(_minLY, ly); _maxLY = Math.Max(_maxLY, ly);
                _minRX = Math.Min(_minRX, rx); _maxRX = Math.Max(_maxRX, rx);
                _minRY = Math.Min(_minRY, ry); _maxRY = Math.Max(_maxRY, ry);
            }
        }
    }

    /// <summary>Écart-type au repos : s'il est élevé, la manette a bougé pendant la mesure.</summary>
    public double CenterNoise
    {
        get
        {
            lock (_lock)
            {
                if (_count < 2) return 0;
                double Var(double sum, double sq) => Math.Max(0, sq / _count - (sum / _count) * (sum / _count));
                return Math.Sqrt(Math.Max(Math.Max(Var(_sumLX, _sqLX), Var(_sumLY, _sqLY)), Math.Max(Var(_sumRX, _sqRX), Var(_sumRY, _sqRY))));
            }
        }
    }

    /// <summary>Résultat combiné avec la calibration précédente (centre seul, ou centre + amplitude).</summary>
    public (StickCalibration Left, StickCalibration Right) Result(StickCalibration prevLeft, StickCalibration prevRight, bool includeRange)
    {
        lock (_lock)
        {
            var left = prevLeft;
            var right = prevRight;
            if (_count > 0)
            {
                left = left with { CenterX = _sumLX / _count, CenterY = _sumLY / _count };
                right = right with { CenterX = _sumRX / _count, CenterY = _sumRY / _count };
            }
            if (includeRange)
            {
                // On n'accepte une amplitude que si le stick a vraiment été tourné
                if (_maxLX - _minLX > 1.2 && _maxLY - _minLY > 1.2)
                    left = left with { MinX = _minLX, MaxX = _maxLX, MinY = _minLY, MaxY = _maxLY };
                if (_maxRX - _minRX > 1.2 && _maxRY - _minRY > 1.2)
                    right = right with { MinX = _minRX, MaxX = _maxRX, MinY = _minRY, MaxY = _maxRY };
            }
            return (left, right);
        }
    }

    /// <summary>Couverture de la rotation (0-1), pour la jauge de progression.</summary>
    public (double Left, double Right) RangeCoverage
    {
        get
        {
            lock (_lock)
            {
                double L = Math.Clamp(Math.Min(_maxLX - _minLX, _maxLY - _minLY) / 1.8, 0, 1);
                double R = Math.Clamp(Math.Min(_maxRX - _minRX, _maxRY - _minRY) / 1.8, 0, 1);
                return (L, R);
            }
        }
    }
}

/// <summary>
/// Détection automatique du drift : un stick immobile (variance quasi nulle) mais décentré
/// au-delà de sa zone morte pendant plusieurs secondes.
/// </summary>
public sealed class DriftDetector
{
    private const int Window = 150;          // ~2,5 s à 60 images/s (alimenté par l'interface)
    private const double StableNoise = 0.012;

    private readonly double[] _lx = new double[Window], _ly = new double[Window], _rx = new double[Window], _ry = new double[Window];
    private int _index, _filled;

    /// <summary>Décalage au repos détecté (0 si aucun), par stick.</summary>
    public double LeftDrift { get; private set; }
    public double RightDrift { get; private set; }

    /// <summary>Décalage mesuré au repos, même faible (NaN si le stick bouge) : sert à l'historique d'usure.</summary>
    public double LeftRest { get; private set; } = double.NaN;
    public double RightRest { get; private set; } = double.NaN;

    /// <summary>Incrémenté à chaque nouvelle évaluation (toutes les 30 mesures).</summary>
    public int Evaluations { get; private set; }

    public void Add(in DualSenseState s, StickCalibration left, StickCalibration right)
    {
        _lx[_index] = StickMath.Normalize(s.LeftX) - left.CenterX;
        _ly[_index] = StickMath.Normalize(s.LeftY) - left.CenterY;
        _rx[_index] = StickMath.Normalize(s.RightX) - right.CenterX;
        _ry[_index] = StickMath.Normalize(s.RightY) - right.CenterY;
        _index = (_index + 1) % Window;
        if (_filled < Window) _filled++;
        if (_index % 30 != 0 || _filled < Window) return;

        (LeftDrift, LeftRest) = Evaluate(_lx, _ly);
        (RightDrift, RightRest) = Evaluate(_rx, _ry);
        Evaluations++;
    }

    private static (double Drift, double RestOffset) Evaluate(double[] xs, double[] ys)
    {
        double mx = xs.Average(), my = ys.Average();
        double noise = Math.Sqrt(xs.Select(v => (v - mx) * (v - mx)).Average() + ys.Select(v => (v - my) * (v - my)).Average());
        double offset = Math.Sqrt(mx * mx + my * my);
        // Immobile, décentré mais pas poussé franchement (un vrai mouvement dépasse largement 0.35)
        bool still = noise < StableNoise && offset < 0.35;
        return (still && offset > 0.04 ? offset : 0, still ? offset : double.NaN);
    }
}
