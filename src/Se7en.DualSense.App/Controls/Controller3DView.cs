using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Se7en.DualSense.App.Services;
using Se7en.DualSense.Hid;

namespace Se7en.DualSense.App.Controls;

/// <summary>
/// Maillage de la manette (Assets/Model, généré par tools/controller-3d) : une pièce par commande mobile,
/// éclairage studio cuit dans une seule texture. Chargé une fois, partagé par toutes les vues.
/// </summary>
public static class ControllerModel
{
    public sealed record Part(string Name, MeshGeometry3D Mesh, Rect3D Bounds);

    private static bool _attempted;
    private static IReadOnlyDictionary<string, Part>? _parts;
    private static Brush? _texture;

    public static bool TryGet(out IReadOnlyDictionary<string, Part> parts, out Brush texture)
    {
        if (!_attempted)
        {
            _attempted = true;
            try
            {
                _parts = LoadParts(new Uri("pack://application:,,,/Assets/Model/dualsense.s7m"));
                _texture = LoadTexture(new Uri("pack://application:,,,/Assets/Model/dualsense-baked.jpg"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UriFormatException)
            {
                _parts = null;
            }
        }
        parts = _parts!;
        texture = _texture!;
        return _parts != null && _texture != null;
    }

    private static Dictionary<string, Part> LoadParts(Uri uri)
    {
        using var raw = Application.GetResourceStream(uri)?.Stream ?? throw new IOException("Modèle absent");
        using var gz = new GZipStream(raw, CompressionMode.Decompress);
        using var r = new BinaryReader(gz);
        if (new string(r.ReadChars(4)) != "S7M1") throw new InvalidDataException("Format de modèle inconnu");
        int count = r.ReadInt32();
        var parts = new Dictionary<string, Part>();
        for (int p = 0; p < count; p++)
        {
            string name = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
            int n = r.ReadInt32();
            float[] pos = ReadFloats(r, n * 3), nrm = ReadFloats(r, n * 3), uv = ReadFloats(r, n * 2);
            int ni = r.ReadInt32();
            var indices = new Int32Collection(ni);
            for (int i = 0; i < ni; i++) indices.Add(r.ReadInt32());

            // Blender (X vers la gauche de la manette, Y vers l'avant, Z vers le haut) → WPF (X droite, Y haut, Z vers l'écran)
            var positions = new Point3DCollection(n);
            var normals = new Vector3DCollection(n);
            var tex = new PointCollection(n);
            for (int i = 0; i < n; i++)
            {
                positions.Add(new Point3D(-pos[i * 3], pos[i * 3 + 2], pos[i * 3 + 1]));
                normals.Add(new Vector3D(-nrm[i * 3], nrm[i * 3 + 2], nrm[i * 3 + 1]));
                tex.Add(new Point(uv[i * 2], 1 - uv[i * 2 + 1]));
            }
            var mesh = new MeshGeometry3D { Positions = positions, Normals = normals, TextureCoordinates = tex, TriangleIndices = indices };
            var bounds = mesh.Bounds;
            mesh.Freeze();
            parts[name] = new Part(name, mesh, bounds);
        }
        return parts;
    }

    private static float[] ReadFloats(BinaryReader r, int count)
    {
        var bytes = r.ReadBytes(count * 4);
        var f = new float[count];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }

    private static Brush LoadTexture(Uri uri)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        var brush = new ImageBrush(image) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// DualSense en 3D temps réel : elle suit les mouvements de la vraie manette (accéléromètre + gyroscope),
/// chaque commande bouge physiquement (sticks qui s'inclinent, boutons qui s'enfoncent, gâchettes qui pivotent),
/// la barre lumineuse prend la couleur réellement envoyée. Glisser à la souris pour la faire tourner.
/// </summary>
public sealed class Controller3DView : Grid
{
    private const double StickAngle = 18;
    private const double TriggerAngle = 24;
    private const double PressDepth = 0.0018;

    private readonly Viewport3D _viewport = new() { ClipToBounds = false, IsHitTestVisible = false };
    private readonly Canvas _glowLayer = new() { IsHitTestVisible = false };
    private readonly Path _glowLeft = new(), _glowRight = new();
    private readonly SolidColorBrush _glowBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _lightbarEmissive = new(Colors.Black);
    private readonly SolidColorBrush _muteEmissive = new(Colors.Black);
    private readonly Ellipse _shadow = new() { IsHitTestVisible = false };
    private readonly ModelVisual3D _visual = new();
    private readonly Transform3DGroup _root = new();
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _roll = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 0, 1), 0);
    private readonly TranslateTransform3D _float = new();
    private readonly List<Mover> _movers = new();
    private Point3D[] _lightbarLeft = [], _lightbarRight = [];

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private double _pitchDeg, _rollDeg, _yawDeg, _basePitch = double.NaN, _baseRoll;
    private double _dragX, _dragY;
    private Point? _dragStart;
    private Point _dragOrigin;

    public bool IsAvailable { get; }

    public Controller3DView()
    {
        IsAvailable = ControllerModel.TryGet(out var parts, out var texture);
        if (!IsAvailable)
        {
            Children.Add(new TextBlock
            {
                Text = "Modèle 3D absent (Assets/Model, voir tools/controller-3d).",
                Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        Background = Brushes.Transparent;
        Cursor = Cursors.SizeAll;
        BuildScene(parts, texture);

        _shadow.Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0x18, 0x1C, 0x2C), Color.FromArgb(0, 0x18, 0x1C, 0x2C));
        _shadow.VerticalAlignment = VerticalAlignment.Top;
        _shadow.HorizontalAlignment = HorizontalAlignment.Center;
        _shadow.RenderTransformOrigin = new Point(0.5, 0.5);
        _shadow.RenderTransform = new ScaleTransform();
        Children.Add(_shadow);
        Children.Add(_viewport);

        foreach (var g in new[] { _glowLeft, _glowRight })
        {
            g.Stroke = _glowBrush;
            g.StrokeThickness = 9;
            g.StrokeStartLineCap = g.StrokeEndLineCap = PenLineCap.Round;
            g.StrokeLineJoin = PenLineJoin.Round;
            g.Effect = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Performance };
            _glowLayer.Children.Add(g);
        }
        Children.Add(_glowLayer);

        SizeChanged += (_, _) => Layout();
        MouseLeftButtonDown += OnDragStart;
        MouseMove += OnDragMove;
        MouseLeftButtonUp += (_, _) => { _dragStart = null; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => _dragStart = null;
    }

    private void BuildScene(IReadOnlyDictionary<string, ControllerModel.Part> parts, Brush texture)
    {
        var body = parts["Body"].Bounds;
        var center = new Point3D(body.X + body.SizeX / 2, body.Y + body.SizeY / 2, body.Z + body.SizeZ / 2);

        // Matériau : texture cuite (éclairée par une lumière ambiante blanche) + reflet spéculaire dynamique
        var specular = new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)), 28);
        specular.Freeze();
        var baked = new DiffuseMaterial(texture);
        baked.Freeze();
        var standard = new MaterialGroup { Children = { baked, specular } };
        standard.Freeze();

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(0xDC, 0xDC, 0xDC)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x3C, 0x3C, 0x40), new Vector3D(-0.35, -0.55, -1)));

        foreach (var part in parts.Values)
        {
            Material material = part.Name switch
            {
                "Lightbar" => new MaterialGroup { Children = { baked, new EmissiveMaterial(_lightbarEmissive) } },
                "Mute" => new MaterialGroup { Children = { baked, specular, new EmissiveMaterial(_muteEmissive) } },
                _ => standard,
            };
            var model = new GeometryModel3D(part.Mesh, material);
            var mover = Mover.For(part, model);
            if (mover != null) _movers.Add(mover);
            group.Children.Add(model);
        }

        // Points de la barre lumineuse (projetés en 2D à chaque image pour la lueur)
        if (parts.TryGetValue("Lightbar", out var lb))
        {
            _lightbarLeft = StripPoints(lb.Mesh.Positions.Where(p => p.X < center.X));
            _lightbarRight = StripPoints(lb.Mesh.Positions.Where(p => p.X >= center.X));
        }

        _root.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        _root.Children.Add(new RotateTransform3D(_yaw));
        _root.Children.Add(new RotateTransform3D(_roll));
        _root.Children.Add(new RotateTransform3D(_pitch));
        _root.Children.Add(_float);
        _visual.Content = group;
        _visual.Transform = _root;
        _viewport.Children.Add(_visual);

        double width = body.SizeX;
        _viewport.Camera = new PerspectiveCamera
        {
            FieldOfView = 15,
            Position = new Point3D(0, 0, width * 0.74 / Math.Tan(7.5 * Math.PI / 180)),
            LookDirection = new Vector3D(0, 0, -1),
            UpDirection = new Vector3D(0, 1, 0),
            NearPlaneDistance = 0.05,
            FarPlaneDistance = 10,
        };
    }

    /// <summary>Huit points moyens le long d'une bande, de haut en bas.</summary>
    private static Point3D[] StripPoints(IEnumerable<Point3D> pts)
    {
        var list = pts.ToList();
        if (list.Count == 0) return [];
        double minY = list.Min(p => p.Y), maxY = list.Max(p => p.Y);
        var result = new List<Point3D>();
        for (int i = 0; i < 8; i++)
        {
            double a = minY + (maxY - minY) * i / 8, b = minY + (maxY - minY) * (i + 1) / 8;
            var bin = list.Where(p => p.Y >= a && p.Y <= b).ToList();
            if (bin.Count == 0) continue;
            result.Add(new Point3D(bin.Average(p => p.X), bin.Average(p => p.Y), bin.Max(p => p.Z)));
        }
        return result.ToArray();
    }

    private void Layout()
    {
        _shadow.Width = ActualWidth * 0.68;
        _shadow.Height = ActualHeight * 0.1;
        _shadow.Margin = new Thickness(0, ActualHeight * 0.66, 0, 0);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragOrigin = new Point(_dragX, _dragY);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetPosition(this);
        _dragX = Math.Clamp(_dragOrigin.X + (p.X - start.X) * 0.35, -70, 70);
        _dragY = Math.Clamp(_dragOrigin.Y + (p.Y - start.Y) * 0.35, -55, 55);
    }

    /// <summary>À appeler à chaque image.</summary>
    public void Update(in FrameData frame)
    {
        if (!IsAvailable) return;
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.1);
        _lastTime = now;
        var s = frame.State;
        bool connected = frame.IsConnected;

        // ── Orientation : accéléromètre (inclinaison) + gyroscope (lacet), recentrée doucement ──
        double targetPitch = 0, targetRoll = 0;
        if (connected && s.IsFullReport && (s.AccelX | s.AccelY | s.AccelZ) != 0)
        {
            double ax = s.AccelX, ay = s.AccelY, az = s.AccelZ;
            double pitch = Math.Atan2(-az, Math.Sqrt(ax * ax + ay * ay)) * 180 / Math.PI;
            double roll = Math.Atan2(ax, Math.Sqrt(ay * ay + az * az)) * 180 / Math.PI;
            if (double.IsNaN(_basePitch)) { _basePitch = pitch; _baseRoll = roll; }
            // La position de tenue habituelle redevient « de face » en quelques secondes
            double k = 1 - Math.Exp(-dt / 3.5);
            _basePitch += (pitch - _basePitch) * k;
            _baseRoll += (roll - _baseRoll) * k;
            targetPitch = Math.Clamp((pitch - _basePitch) * 0.9, -38, 38);
            targetRoll = Math.Clamp((roll - _baseRoll) * 0.9, -38, 38);
            _yawDeg += -s.GyroY / 16.0 * dt * 0.9;
        }
        else
        {
            _basePitch = double.NaN;
        }
        _yawDeg *= Math.Exp(-dt / 1.6);
        _yawDeg = Math.Clamp(_yawDeg, -40, 40);

        // Balancement de présentation + rotation à la souris (qui revient en place une fois lâchée)
        double idlePitch = 2.5 * Math.Sin(now * 2 * Math.PI / 7.0);
        double idleRoll = 5 * Math.Sin(now * 2 * Math.PI / 9.0);
        if (_dragStart == null)
        {
            double back = Math.Exp(-dt / 0.35);
            _dragX *= back;
            _dragY *= back;
        }
        double smooth = 1 - Math.Exp(-dt / 0.08);
        _pitchDeg += (targetPitch + idlePitch + _dragY - _pitchDeg) * smooth;
        _rollDeg += (targetRoll + idleRoll + _dragX - _rollDeg) * smooth;
        _pitch.Angle = 12 + _pitchDeg;          // légère vue plongeante, plus « produit »
        _roll.Angle = _rollDeg;
        _yaw.Angle = _yawDeg;

        double phase = Math.Sin(now * 2 * Math.PI / 3.6);
        _float.OffsetY = 0.004 * phase;
        if (_shadow.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = st.ScaleY = 1 - 0.08 * (phase + 1) / 2;
            _shadow.Opacity = 0.95 - 0.3 * (phase + 1) / 2;
        }

        // ── Commandes ──
        double press = 1 - Math.Exp(-dt / 0.035);
        var buttons = connected ? s.Buttons : DualSenseButtons.None;
        foreach (var m in _movers)
            m.Step(s, buttons, connected, press);

        // ── Barre lumineuse et LED micro ──
        var o = frame.Output;
        var color = connected ? Color.FromRgb(o.LightbarRed, o.LightbarGreen, o.LightbarBlue) : Colors.Black;
        bool lit = color.R + color.G + color.B > 24;
        _lightbarEmissive.Color = lit ? Color.FromRgb((byte)(color.R * 0.85), (byte)(color.G * 0.85), (byte)(color.B * 0.85)) : Colors.Black;
        _muteEmissive.Color = connected && o.MuteLed ? Color.FromRgb(0xE0, 0x60, 0x10) : Colors.Black;
        double breathe = 0.45 + 0.2 * (Math.Sin(now * 2 * Math.PI / 2.8) + 1) / 2;
        _glowBrush.Color = Color.FromArgb(lit ? (byte)(255 * breathe) : (byte)0, color.R, color.G, color.B);
        if (lit) UpdateGlow();
    }

    private void UpdateGlow()
    {
        var transform = _visual.TransformToAncestor(_viewport);
        if (transform == null) return;
        _glowLeft.Data = Strip(transform, _lightbarLeft);
        _glowRight.Data = Strip(transform, _lightbarRight);
    }

    private static Geometry? Strip(GeneralTransform3DTo2D t, Point3D[] pts)
    {
        if (pts.Length < 2) return null;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < pts.Length; i++)
            {
                if (!t.TryTransform(pts[i], out var p)) return null;
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>Animation physique d'une pièce (inclinaison, enfoncement, pivot).</summary>
    private sealed class Mover
    {
        private readonly Func<DualSenseState, DualSenseButtons, bool, (double A, double B, double C)> _target;
        private readonly Action<double, double, double> _apply;
        private double _a, _b, _c;

        private Mover(Func<DualSenseState, DualSenseButtons, bool, (double, double, double)> target, Action<double, double, double> apply)
        {
            _target = target;
            _apply = apply;
        }

        public void Step(in DualSenseState s, DualSenseButtons buttons, bool connected, double k)
        {
            var (a, b, c) = _target(s, buttons, connected);
            double na = _a + (a - _a) * k, nb = _b + (b - _b) * k, nc = _c + (c - _c) * k;
            if (Math.Abs(na - _a) + Math.Abs(nb - _b) + Math.Abs(nc - _c) < 1e-5 && Math.Abs(a - na) + Math.Abs(b - nb) + Math.Abs(c - nc) < 1e-4)
                return;
            _a = na; _b = nb; _c = nc;
            _apply(_a, _b, _c);
        }

        private static double Axis(byte v) => Math.Clamp((v - 127.5) / 127.5, -1, 1);

        public static Mover? For(ControllerModel.Part part, GeometryModel3D model)
        {
            var b = part.Bounds;
            double cx = b.X + b.SizeX / 2, cy = b.Y + b.SizeY / 2;

            Mover Press(DualSenseButtons flag, Vector3D direction)
            {
                var t = new TranslateTransform3D();
                model.Transform = t;
                return new Mover((_, buttons, _) => ((buttons & flag) != 0 ? 1 : 0, 0, 0),
                    (a, _, _) => { t.OffsetX = direction.X * a; t.OffsetY = direction.Y * a; t.OffsetZ = direction.Z * a; });
            }

            Mover Stick(bool left, DualSenseButtons click)
            {
                var pivot = new Point3D(cx, cy, b.Z - 0.02);
                var rx = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                var ry = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
                var push = new TranslateTransform3D();
                model.Transform = new Transform3DGroup
                {
                    Children = { new RotateTransform3D(rx, pivot), new RotateTransform3D(ry, pivot), push },
                };
                return new Mover((s, buttons, on) => on
                        ? (left ? Axis(s.LeftX) : Axis(s.RightX), left ? Axis(s.LeftY) : Axis(s.RightY), (buttons & click) != 0 ? 1 : 0)
                        : (0, 0, 0),
                    (x, y, c) => { ry.Angle = x * StickAngle; rx.Angle = y * StickAngle; push.OffsetZ = -c * PressDepth; });
            }

            Mover Trigger(bool left)
            {
                // Charnière en haut de la gâchette ; la pression ramène le bas vers l'avant
                var hinge = new Point3D(cx, b.Y + b.SizeY, b.Z + b.SizeZ * 0.7);
                var rot = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
                model.Transform = new RotateTransform3D(rot, hinge);
                return new Mover((s, _, on) => (on ? (left ? s.L2 : s.R2) / 255.0 : 0, 0, 0), (a, _, _) => rot.Angle = -a * TriggerAngle);
            }

            var down = new Vector3D(0, 0, -PressDepth);
            return part.Name switch
            {
                "StickL" => Stick(true, DualSenseButtons.L3),
                "StickR" => Stick(false, DualSenseButtons.R3),
                "L2" => Trigger(true),
                "R2" => Trigger(false),
                "L1" => Press(DualSenseButtons.L1, new Vector3D(0, -PressDepth, 0)),
                "R1" => Press(DualSenseButtons.R1, new Vector3D(0, -PressDepth, 0)),
                "Cross" => Press(DualSenseButtons.Cross, down),
                "Circle" => Press(DualSenseButtons.Circle, down),
                "Square" => Press(DualSenseButtons.Square, down),
                "Triangle" => Press(DualSenseButtons.Triangle, down),
                "DpadUp" => Press(DualSenseButtons.DpadUp, down),
                "DpadDown" => Press(DualSenseButtons.DpadDown, down),
                "DpadLeft" => Press(DualSenseButtons.DpadLeft, down),
                "DpadRight" => Press(DualSenseButtons.DpadRight, down),
                "Create" => Press(DualSenseButtons.Create, down),
                "Options" => Press(DualSenseButtons.Options, down),
                "PS" => Press(DualSenseButtons.PS, down),
                "Mute" => Press(DualSenseButtons.Mute, down),
                "Touchpad" => Press(DualSenseButtons.Touchpad, down),
                "Lightbar" => Press(DualSenseButtons.Touchpad, down),
                _ => null,
            };
        }
    }
}
