using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text.Json;

namespace PowerMonitor;

public sealed class Settings
{
    public int BaselineWatts { get; set; } = 40;      // 主板/内存/风扇/硬盘等无法读取的部分
    public int PsuEfficiency { get; set; } = 90;      // 电源转换效率 %
    public bool TopMost { get; set; } = true;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }
}

/// <summary>累计能耗，持久化到 energy.json。</summary>
public sealed class EnergyStore
{
    public string Day { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public double TodayWh { get; set; }
    public double TotalWh { get; set; }
    public DateTime Since { get; set; } = DateTime.Now;

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "energy.json");
    public static EnergyStore Load()
    {
        try { return JsonSerializer.Deserialize<EnergyStore>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }

    public void Add(double wh)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (Day != today) { Day = today; TodayWh = 0; }
        TodayWh += wh;
        TotalWh += wh;
    }
}

public sealed class MainForm : Form
{
    private const int W = 300, Pad = 12, RowH = 28, HeaderH = 96, GraphH = 48, EnergyH = 52, HistoryLen = 120;

    private readonly EnergyStore _energy = EnergyStore.Load();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _sessionWh;
    private int _ticksSinceSave;

    private readonly PowerReader _reader = new();
    private readonly Settings _cfg = Settings.Load();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly NotifyIcon _tray = new();
    private readonly Queue<float> _history = new();
    private readonly Dictionary<string, float> _peak = new();

    private List<PowerItem> _items = [];
    private float _total, _componentSum;
    private string _totalSource = "";
    private float _maxTotal = 1;
    private Point? _dragStart;
    private bool _pressClose, _hoverClose;

    private static readonly Color Bg = Color.FromArgb(24, 26, 31);
    private static readonly Color Fg = Color.FromArgb(230, 232, 236);
    private static readonly Color Dim = Color.FromArgb(130, 136, 148);
    private static readonly Color Track = Color.FromArgb(40, 43, 50);
    private static readonly Color Hairline = Color.FromArgb(48, 52, 60);
    private static readonly Dictionary<string, Color> KindColor = new()
    {
        ["CPU"] = Color.FromArgb(66, 153, 225),
        ["GPU"] = Color.FromArgb(72, 187, 120),
        ["RAM"] = Color.FromArgb(237, 137, 54),
        ["DISK"] = Color.FromArgb(159, 122, 234),
        ["PSU"] = Color.FromArgb(245, 101, 101),
        ["BAT"] = Color.FromArgb(236, 201, 75),
        ["MISC"] = Color.FromArgb(160, 174, 192),
    };
    private static readonly Color Accent = Color.FromArgb(246, 173, 85);

    private readonly Font _fBig = new("Segoe UI Semibold", 28f);
    private readonly Font _fSmall = new("Microsoft YaHei UI", 8.5f);
    private readonly Font _fRow = new("Microsoft YaHei UI", 9f);
    private readonly Font _fVal = new("Consolas", 10f, FontStyle.Bold);

    private Rectangle CloseRect => new(W - Pad - 17, 11, 16, 16);

    public MainForm()
    {
        Text = "功耗监控";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Bg;
        DoubleBuffered = true;
        ShowInTaskbar = true;
        TopMost = _cfg.TopMost;
        StartPosition = FormStartPosition.Manual;
        Icon = MakeIcon("W");
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = _cfg.X >= 0 ? new Point(_cfg.X, _cfg.Y) : new Point(wa.Right - W - 20, wa.Top + 20);
        Size = new Size(W, HeaderH + GraphH + EnergyH + RowH + Pad);
        TryRoundCorners();

        ContextMenuStrip = BuildMenu();
        _tray.ContextMenuStrip = ContextMenuStrip;
        _tray.Visible = true;
        _tray.Text = "功耗监控";
        _tray.Icon = Icon;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleVisible(); };

        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (CloseRect.Contains(e.Location)) { _pressClose = true; return; }
            _dragStart = e.Location;
        };
        MouseMove += (_, e) =>
        {
            bool hov = CloseRect.Contains(e.Location);
            if (hov != _hoverClose) { _hoverClose = hov; Invalidate(CloseRect); }
            if (e.Button == MouseButtons.Left && !_pressClose && _dragStart.HasValue)
                Location = new Point(Location.X + e.X - _dragStart.Value.X, Location.Y + e.Y - _dragStart.Value.Y);
        };
        MouseUp += (_, e) =>
        {
            if (_pressClose)
            {
                _pressClose = false;
                if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location)) Close();
                return;
            }
            _dragStart = null;
            _cfg.X = Left; _cfg.Y = Top; _cfg.Save();
        };
        MouseLeave += (_, _) => { if (_hoverClose) { _hoverClose = false; Invalidate(CloseRect); } };

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        var top = new ToolStripMenuItem("窗口置顶") { Checked = _cfg.TopMost, CheckOnClick = true };
        top.CheckedChanged += (_, _) => { TopMost = _cfg.TopMost = top.Checked; _cfg.Save(); };
        m.Items.Add("显示 / 隐藏", null, (_, _) => ToggleVisible());
        m.Items.Add(top);
        m.Items.Add("整机估算设置...", null, (_, _) => ShowSettings());
        m.Items.Add("重置峰值", null, (_, _) => { _peak.Clear(); _maxTotal = 1; });
        m.Items.Add("清零累计能耗", null, (_, _) =>
        {
            if (MessageBox.Show(this, "确定清零累计能耗（含今日）？", "功耗监控", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _energy.TodayWh = _energy.TotalWh = _sessionWh = 0;
            _energy.Since = DateTime.Now;
            _energy.Save();
        });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("退出", null, (_, _) => Close());
        return m;
    }

    private void ToggleVisible()
    {
        Visible = !Visible;
        if (Visible) Activate();
    }

    private void Tick()
    {
        try
        {
            _reader.Update();
            _items = _reader.Read()
                .OrderBy(i => Array.IndexOf(new[] { "PSU", "BAT", "CPU", "GPU", "RAM", "DISK", "MISC" }, i.Kind))
                .ThenByDescending(i => i.Watts).ToList();
        }
        catch { /* 传感器偶发读取失败时保留上次数据 */ }

        ComputeTotal();

        // 按实际经过时间积分；间隔过长（睡眠/休眠唤醒）时不计入
        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (dt < 10)
        {
            double wh = _total * dt / 3600.0;
            _sessionWh += wh;
            _energy.Add(wh);
        }
        if (++_ticksSinceSave >= 60) { _ticksSinceSave = 0; _energy.Save(); }

        _history.Enqueue(_total);
        while (_history.Count > HistoryLen) _history.Dequeue();
        _maxTotal = Math.Max(_maxTotal, _total);
        foreach (var i in _items)
            _peak[i.Key] = Math.Max(_peak.GetValueOrDefault(i.Key), i.Watts);

        int rows = _items.Count(i => i.Kind is not ("PSU" or "BAT"));
        int h = HeaderH + GraphH + EnergyH + Math.Max(rows, 1) * RowH + Pad;
        if (Height != h) Height = h;

        var tip = $"整机 {_total:0} W";
        foreach (var i in _items.Where(i => i.Kind is "CPU" or "GPU")) tip += $"\n{i.Kind} {i.Watts:0} W";
        tip += $"\n今日 {FormatEnergy(_energy.TodayWh)}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
        Invalidate();
    }

    private void ComputeTotal()
    {
        var psu = _items.FirstOrDefault(i => i.Kind == "PSU");
        var bat = _items.FirstOrDefault(i => i.Kind == "BAT");
        // 核显功耗已包含在 CPU Package 内，避免重复计入
        _componentSum = _items.Where(i => i.Kind is not ("PSU" or "BAT") && !IsIntegratedGpu(i)).Sum(i => i.Watts);

        if (psu != null) { _total = psu.Watts; _totalSource = "电源传感器实测"; }
        else if (bat != null && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline)
        { _total = bat.Watts; _totalSource = "电池放电实测"; }
        else
        {
            _total = (_componentSum + _cfg.BaselineWatts) / (_cfg.PsuEfficiency / 100f);
            _totalSource = $"估算 · 墙插功率 (基础{_cfg.BaselineWatts}W, 效率{_cfg.PsuEfficiency}%)";
        }
    }

    private bool IsIntegratedGpu(PowerItem i) =>
        i.Kind == "GPU" && _items.Any(x => x.Kind == "CPU") &&
        (i.Name.Equals("Radeon Graphics", StringComparison.OrdinalIgnoreCase) || i.Key.Contains("gpu-intel"));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        void DrawStat(float y, string label, string value)
        {
            var vw = g.MeasureString(value, _fSmall).Width;
            var lw = g.MeasureString(label, _fSmall).Width;
            g.DrawString(value, _fSmall, new SolidBrush(Fg), W - Pad - vw, y);
            g.DrawString(label, _fSmall, new SolidBrush(Dim), W - Pad - vw - lw - 4, y);
        }

        // 圆角边框
        using (var bp = RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9))
        using (var pen = new Pen(Hairline))
            g.DrawPath(pen, bp);

        // ── 头部 ──
        using (var b = new SolidBrush(Color.FromArgb(72, 187, 120)))
            g.FillEllipse(b, Pad, Pad + 3, 6, 6);                       // 运行状态点
        g.DrawString("整机功耗", _fSmall, new SolidBrush(Dim), Pad + 10, Pad - 1);

        var cr = CloseRect;
        using (var path = RoundRect(cr, 4))
        using (var b = new SolidBrush(_hoverClose ? Color.FromArgb(197, 78, 78) : Color.FromArgb(42, 46, 54)))
            g.FillPath(b, path);
        g.DrawString("×", _fRow, new SolidBrush(_hoverClose ? Color.White : Dim), cr,
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

        string totalTxt = $"{_total:0}";
        g.DrawString(totalTxt, _fBig, new SolidBrush(Accent), Pad - 3, Pad + 15);
        var tw = g.MeasureString(totalTxt, _fBig);
        g.DrawString("W", _fRow, new SolidBrush(Dim), Pad - 3 + tw.Width - 4, Pad + 40);

        DrawStat(Pad + 22, "峰值", $"{_maxTotal:0} W");
        DrawStat(Pad + 39, "组件", $"{_componentSum:0} W");

        // 数据来源 chip
        var sw = g.MeasureString(_totalSource, _fSmall);
        var chipRect = new RectangleF(Pad, Pad + 58, Math.Min(sw.Width + 12, W - Pad * 2), 17);
        using (var path = RoundRect(chipRect, 8))
        using (var b = new SolidBrush(Track))
            g.FillPath(b, path);
        g.DrawString(_totalSource, _fSmall, new SolidBrush(Dim),
            new RectangleF(Pad + 6, Pad + 60, chipRect.Width - 10, 14),
            new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

        // ── 历史曲线 ──
        var gr = new Rectangle(Pad, HeaderH, W - Pad * 2, GraphH);
        using (var path = RoundRect(gr, 6))
        using (var b = new SolidBrush(Track))
            g.FillPath(b, path);
        using (var pen = new Pen(Hairline))
            g.DrawLine(pen, gr.Left + 4, gr.Top + gr.Height / 2f, gr.Right - 4, gr.Top + gr.Height / 2f);
        if (_history.Count > 1)
        {
            var arr = _history.ToArray();
            float max = Math.Max(arr.Max() * 1.15f, 10);
            float step = gr.Width / (float)(HistoryLen - 1);
            float x0 = gr.Right - (arr.Length - 1) * step;
            var pts = arr.Select((v, i) => new PointF(x0 + i * step, gr.Bottom - 4 - v / max * (gr.Height - 10))).ToList();

            var oldClip = g.Clip;
            using (var cpath = RoundRect(gr, 6))
                g.SetClip(cpath);
            using (var fill = new GraphicsPath())
            {
                if (pts.Count > 2) fill.AddCurve(pts.ToArray(), 0.5f);
                else fill.AddLines(pts.ToArray());
                fill.AddLine(pts[^1], new PointF(pts[^1].X, gr.Bottom));
                fill.AddLine(new PointF(pts[^1].X, gr.Bottom), new PointF(pts[0].X, gr.Bottom));
                using var lg = new LinearGradientBrush(gr, Color.FromArgb(70, Accent), Color.FromArgb(6, Accent), LinearGradientMode.Vertical);
                g.FillPath(lg, fill);
            }
            using (var pen = new Pen(Accent, 1.6f) { LineJoin = LineJoin.Round })
            {
                if (pts.Count > 2) g.DrawCurve(pen, pts.ToArray(), 0.5f);
                else g.DrawLines(pen, pts.ToArray());
            }
            g.Clip = oldClip;
            using (var b = new SolidBrush(Accent))
                g.FillEllipse(b, pts[^1].X - 2.5f, pts[^1].Y - 2.5f, 5, 5);  // 最新值亮点
        }

        // ── 能耗累计：本次 / 今日 / 累计 ──
        int ey = HeaderH + GraphH + 10;
        var cols = new[]
        {
            ("本次", _sessionWh),
            ("今日", _energy.TodayWh),
            ($"累计 · 自{_energy.Since:M/d}", _energy.TotalWh),
        };
        float cw = (W - Pad * 2) / 3f;
        for (int c = 0; c < cols.Length; c++)
        {
            float cx = Pad + c * cw;
            if (c > 0)
                using (var pen = new Pen(Hairline))
                    g.DrawLine(pen, cx - 7, ey + 2, cx - 7, ey + 32);
            g.DrawString(cols[c].Item1, _fSmall, new SolidBrush(Dim),
                new RectangleF(cx, ey, cw - 12, 14),
                new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            g.DrawString(FormatEnergy(cols[c].Item2), _fVal, new SolidBrush(Fg), cx, ey + 16);
        }

        // ── 各硬件 ──
        int y = HeaderH + GraphH + EnergyH;
        var rows = _items.Where(i => i.Kind is not ("PSU" or "BAT")).ToList();
        if (rows.Count == 0)
            g.DrawString("未读取到功耗传感器，请以管理员身份运行", _fSmall, new SolidBrush(Dim), Pad, y + 6);
        foreach (var i in rows)
        {
            var col = KindColor[i.Kind];
            using (var path = RoundRect(new RectangleF(Pad, y + 5, 4, 14), 2))
            using (var b = new SolidBrush(col))
                g.FillPath(b, path);

            var val = $"{i.Watts:0.0} W";
            var vw = g.MeasureString(val, _fVal);
            g.DrawString(val, _fVal, new SolidBrush(col), W - Pad - vw.Width, y + 2);

            var label = IsIntegratedGpu(i) ? $"{i.Name} (含于CPU)" : i.Name;
            g.DrawString(label, _fRow, new SolidBrush(Fg),
                new RectangleF(Pad + 11, y + 3, W - Pad * 2 - 11 - vw.Width - 8, 16),
                new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

            // 底部细条：当前值 / 本次峰值
            float peak = Math.Max(_peak.GetValueOrDefault(i.Key), 1);
            var bar = new RectangleF(Pad + 11, y + RowH - 5, W - Pad * 2 - 11, 3);
            using (var path = RoundRect(bar, 1.5f))
            using (var b = new SolidBrush(Track))
                g.FillPath(b, path);
            float fillW = bar.Width * Math.Min(i.Watts / peak, 1);
            if (fillW > 3)
            {
                using var path = RoundRect(new RectangleF(bar.X, bar.Y, fillW, 3), 1.5f);
                using var b = new SolidBrush(col);
                g.FillPath(b, path);
            }
            y += RowH;
        }
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Windows 11 上让无边框窗口使用系统圆角；旧系统静默忽略。</summary>
    private void TryRoundCorners()
    {
        try
        {
            int pref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static string FormatEnergy(double wh) =>
        wh >= 1000 ? $"{wh / 1000:0.000} kWh" : $"{wh:0.0} Wh";

    private void ShowSettings()
    {
        using var f = new Form
        {
            Text = "整机估算设置", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(300, 150), TopMost = true,
            Font = _fRow,
        };
        var tip = new Label { Text = "无电源/电池传感器时：整机 = (组件合计 + 基础功耗) ÷ 效率", Left = 12, Top = 10, Width = 280, Height = 34 };
        var l1 = new Label { Text = "基础功耗 (W)", Left = 12, Top = 52, Width = 120 };
        var n1 = new NumericUpDown { Left = 160, Top = 48, Width = 120, Minimum = 0, Maximum = 500, Value = _cfg.BaselineWatts };
        var l2 = new Label { Text = "电源效率 (%)", Left = 12, Top = 84, Width = 120 };
        var n2 = new NumericUpDown { Left = 160, Top = 80, Width = 120, Minimum = 50, Maximum = 100, Value = _cfg.PsuEfficiency };
        var ok = new Button { Text = "确定", Left = 205, Top = 114, Width = 75, DialogResult = DialogResult.OK };
        f.Controls.AddRange([tip, l1, n1, l2, n2, ok]);
        f.AcceptButton = ok;
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            _cfg.BaselineWatts = (int)n1.Value;
            _cfg.PsuEfficiency = (int)n2.Value;
            _cfg.Save();
        }
    }

    private static Icon MakeIcon(string text)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var b = new SolidBrush(Accent);
            g.FillEllipse(b, 1, 1, 30, 30);
            using var f = new Font("Segoe UI", 15f, FontStyle.Bold);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, f, Brushes.Black, new RectangleF(0, 1, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _energy.Save();
        _tray.Visible = false;
        _tray.Dispose();
        _reader.Dispose();
        base.OnFormClosing(e);
    }
}
