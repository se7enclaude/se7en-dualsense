using System.Diagnostics;
using System.Text;
using Se7en.DualSense.Hid;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "se7en DualSense — test console";
Console.CursorVisible = false;
Console.Clear();

var log = new LinkedList<string>();
void AddLog(string msg)
{
    lock (log)
    {
        log.AddLast($"{DateTime.Now:HH:mm:ss}  {msg}");
        while (log.Count > 5) log.RemoveFirst();
    }
}

using var connection = new DualSenseConnection();
connection.Log += AddLog;
connection.Start();

(byte R, byte G, byte B)[] colors =
[
    (0, 64, 255), (255, 0, 0), (0, 255, 0), (255, 0, 255), (255, 160, 0), (255, 255, 255),
];
int colorIndex = 0;
bool rumble = false;

void ApplyOutput()
{
    var c = colors[colorIndex];
    connection.SetOutput(new DualSenseOutputState
    {
        LightbarRed = c.R, LightbarGreen = c.G, LightbarBlue = c.B,
        PlayerLeds = 0x04,
        HeavyMotor = rumble ? (byte)200 : (byte)0,
        LightMotor = rumble ? (byte)120 : (byte)0,
    });
}

var rateWatch = Stopwatch.StartNew();
long lastCount = 0;
double rate = 0;
bool running = true;

while (running)
{
    while (Console.KeyAvailable)
    {
        switch (Console.ReadKey(true).Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                running = false;
                break;
            case ConsoleKey.L:
                colorIndex = (colorIndex + 1) % colors.Length;
                ApplyOutput();
                AddLog($"Barre lumineuse → RGB({colors[colorIndex].R}, {colors[colorIndex].G}, {colors[colorIndex].B})");
                break;
            case ConsoleKey.V:
                rumble = !rumble;
                ApplyOutput();
                AddLog(rumble ? "Vibrations ON" : "Vibrations OFF");
                break;
        }
    }

    if (rateWatch.ElapsedMilliseconds >= 1000)
    {
        long count = connection.ReportCount;
        rate = (count - lastCount) * 1000.0 / rateWatch.ElapsedMilliseconds;
        lastCount = count;
        rateWatch.Restart();
    }

    Render(connection, rate);
    Thread.Sleep(33);
}

Console.CursorVisible = true;
Console.Clear();

void Render(DualSenseConnection conn, double hz)
{
    var sb = new StringBuilder(4096);
    var info = conn.Device;
    var s = conn.Latest;

    sb.AppendLine("══════════════ se7en DualSense — Phase 1 : entrées brutes ══════════════");
    if (info == null)
    {
        sb.AppendLine("Manette     : non connectée");
    }
    else
    {
        sb.AppendLine($"Manette     : {info.DisplayName}   Connexion : {info.ConnectionLabel}   " +
                      $"Rapport : {(s.IsFullReport ? "complet" : "réduit")}   Fréquence : {hz,6:0} Hz");
        string charge = s.Charging switch
        {
            ChargingState.Charging => "en charge",
            ChargingState.Full => "chargée",
            ChargingState.Error => "erreur",
            _ => "sur batterie",
        };
        sb.AppendLine(s.IsFullReport
            ? $"Batterie    : {s.BatteryPercent,3} % ({charge})"
            : "Batterie    : indisponible (rapport réduit)");
    }
    sb.AppendLine();
    sb.AppendLine($"Stick G     : X={s.LeftX,3}  Y={s.LeftY,3}   {Bar(s.LeftX)} {Bar(s.LeftY)}");
    sb.AppendLine($"Stick D     : X={s.RightX,3}  Y={s.RightY,3}   {Bar(s.RightX)} {Bar(s.RightY)}");
    sb.AppendLine($"L2          : {s.L2,3}  {Bar(s.L2)}");
    sb.AppendLine($"R2          : {s.R2,3}  {Bar(s.R2)}");
    sb.AppendLine();
    sb.AppendLine("Boutons     : " + ButtonsLine(s.Buttons));
    sb.AppendLine();
    sb.AppendLine($"Pavé tact.  : doigt 1 {Touch(s.Touch0)}   doigt 2 {Touch(s.Touch1)}");
    sb.AppendLine($"Gyroscope   : {s.GyroX,6} {s.GyroY,6} {s.GyroZ,6}    Accéléro : {s.AccelX,6} {s.AccelY,6} {s.AccelZ,6}");
    sb.AppendLine();
    sb.AppendLine("Touches : [L] changer la couleur de la barre   [V] tester les vibrations   [Q] quitter");
    sb.AppendLine("──────────────────────────────── Journal ───────────────────────────────");
    lock (log)
        foreach (var line in log)
            sb.AppendLine(line);

    // Remplir chaque ligne pour effacer les restes de l'affichage précédent
    int width = Math.Max(20, Console.WindowWidth - 1);
    var output = new StringBuilder();
    foreach (var line in sb.ToString().Split(Environment.NewLine))
        output.AppendLine(line.Length > width ? line[..width] : line.PadRight(width));
    for (int i = 0; i < 3; i++)
        output.AppendLine(new string(' ', width));

    Console.SetCursorPosition(0, 0);
    Console.Write(output.ToString());
}

static string Bar(byte v)
{
    const int len = 16;
    int filled = v * len / 255;
    return "[" + new string('█', filled) + new string('·', len - filled) + "]";
}

static string Touch(TouchPoint t) =>
    t.Active ? $"(id {t.Id,3}) x={t.X,4} y={t.Y,4}" : "—                     ";

static string ButtonsLine(DualSenseButtons b)
{
    (DualSenseButtons Flag, string Label)[] all =
    [
        (DualSenseButtons.Cross, "✕"), (DualSenseButtons.Circle, "○"), (DualSenseButtons.Square, "□"),
        (DualSenseButtons.Triangle, "△"), (DualSenseButtons.DpadUp, "↑"), (DualSenseButtons.DpadDown, "↓"),
        (DualSenseButtons.DpadLeft, "←"), (DualSenseButtons.DpadRight, "→"), (DualSenseButtons.L1, "L1"),
        (DualSenseButtons.R1, "R1"), (DualSenseButtons.L2, "L2"), (DualSenseButtons.R2, "R2"),
        (DualSenseButtons.L3, "L3"), (DualSenseButtons.R3, "R3"), (DualSenseButtons.Create, "Create"),
        (DualSenseButtons.Options, "Options"), (DualSenseButtons.PS, "PS"), (DualSenseButtons.Touchpad, "Pavé"),
        (DualSenseButtons.Mute, "Micro"), (DualSenseButtons.FnLeft, "FnG"), (DualSenseButtons.FnRight, "FnD"),
        (DualSenseButtons.PaddleLeft, "PalG"), (DualSenseButtons.PaddleRight, "PalD"),
    ];
    var parts = all.Where(x => (b & x.Flag) != 0).Select(x => x.Label).ToArray();
    return parts.Length == 0 ? "(aucun)" : string.Join(" ", parts);
}
