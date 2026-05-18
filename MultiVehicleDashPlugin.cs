using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using MissionPlanner;
using MissionPlanner.Utilities;
using MissionPlanner.Controls;

namespace MultiVehicleDash
{
    public class MultiVehicleDashPlugin : MissionPlanner.Plugin.Plugin
    {
        private MultiVehicleDashForm _dashForm;
        private Dictionary<string, int> _keyToPanelIndex = new Dictionary<string, int>();
        private VehicleData[] _currentData = new VehicleData[10];

        public override string Name { get { return "Multi-Vehicle Dashboard"; } }
        public override string Version { get { return "1.1"; } }
        public override string Author { get { return "ZeroGravity"; } }

        public override bool Init()
        {
            loopratehz = 5;
            return true;
        }

        public override bool Loaded()
        {
            var menuItem = new ToolStripMenuItem("Multi-Vehicle Dash");
            menuItem.Click += (s, e) => ShowDashboard();
            Host.FDMenuMap.Items.Add(menuItem);

            Host.MainForm.BeginInvoke((MethodInvoker)(() => ShowDashboard()));

            return true;
        }

        private void ShowDashboard()
        {
            if (_dashForm == null || _dashForm.IsDisposed)
            {
                _dashForm = new MultiVehicleDashForm(ResetMapping);
                _dashForm.Show();
            }
            else
            {
                _dashForm.BringToFront();
            }
        }

        private void ResetMapping()
        {
            _keyToPanelIndex.Clear();
            for (int i = 0; i < 10; i++) _currentData[i] = null;
        }

        public override bool Loop()
        {
            if (_dashForm == null || _dashForm.IsDisposed)
                return true;

            try
            {
                // Mark existing as disconnected temporarily
                for (int i = 0; i < 10; i++)
                {
                    if (_currentData[i] != null)
                        _currentData[i].IsConnected = false;
                }

                int portIdx = 0;
                foreach (var port in MainV2.Comports)
                {
                    try
                    {
                        if (port == null || port.BaseStream == null || !port.BaseStream.IsOpen || port.MAVlist == null) { portIdx++; continue; }

                        string portName = "N/A";
                        // Fix implemented here: Grabbing the actual PortName instead of calling ToString() on the stream
                        try { portName = port.BaseStream?.PortName ?? "N/A"; } catch { }

                        foreach (var mav in port.MAVlist)
                        {
                            try
                            {
                                if (mav == null || mav.cs == null) continue;
                                var cs = mav.cs;
                                string key = string.Format("{0}_{1}_{2}", portIdx, mav.sysid, mav.compid);

                                int panelIndex = -1;
                                if (_keyToPanelIndex.ContainsKey(key))
                                {
                                    panelIndex = _keyToPanelIndex[key];
                                }
                                else
                                {
                                    for (int i = 0; i < 10; i++)
                                    {
                                        if (_currentData[i] == null)
                                        {
                                            panelIndex = i;
                                            _keyToPanelIndex[key] = i;
                                            break;
                                        }
                                    }
                                }

                                if (panelIndex >= 0 && panelIndex < 10)
                                {
                                    if (_currentData[panelIndex] == null)
                                        _currentData[panelIndex] = new VehicleData();

                                    var d = _currentData[panelIndex];
                                    d.UniqueKey = key;
                                    d.SysId = mav.sysid;
                                    d.CompId = mav.compid;
                                    d.PortName = portName;
                                    d.Roll = cs.roll;
                                    d.Pitch = cs.pitch;
                                    d.Altitude = cs.alt;
                                    d.Groundspeed = cs.groundspeed;
                                    d.BatteryVoltage = cs.battery_voltage;
                                    d.SatCount = cs.satcount;
                                    
                                    // Fix implemented here: cleaner null coalescing fallback
                                    d.Mode = cs.mode ?? "UNKNOWN"; 
                                    
                                    d.Armed = cs.armed;
                                    d.ComPort = port;
                                    d.IsConnected = cs.connected;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    portIdx++;
                }

                if (_dashForm != null && !_dashForm.IsDisposed)
                {
                    VehicleData[] copy = new VehicleData[10];
                    for(int i = 0; i < 10; i++)
                    {
                        if (_currentData[i] != null)
                            copy[i] = _currentData[i].Clone();
                    }

                    _dashForm.BeginInvoke((MethodInvoker)(() =>
                    {
                        _dashForm.UpdateVehicles(copy);
                    }));
                }
            }
            catch { }

            return true;
        }

        public override bool Exit()
        {
            if (_dashForm != null && !_dashForm.IsDisposed)
                _dashForm.Close();
            return true;
        }
    }

    public class VehicleData
    {
        public string UniqueKey;
        public byte SysId;
        public byte CompId;
        public string PortName;
        public float Roll, Pitch;
        public float Altitude, Groundspeed;
        public double BatteryVoltage;
        public float SatCount;
        public string Mode;
        public bool Armed;
        public MAVLinkInterface ComPort;
        public bool IsConnected;

        public VehicleData Clone()
        {
            return (VehicleData)this.MemberwiseClone();
        }
    }

    public class MultiVehicleDashForm : Form
    {
        private TableLayoutPanel _gridPanel;
        private Label _titleLabel, _statusLabel, _timeLabel;
        private Button _globalArmBtn, _globalDisarmBtn;
        private DronePanel[] _panels = new DronePanel[10];
        private System.Windows.Forms.Timer _clockTimer;
        private int _armClickCount = 0;
        private DateTime _lastArmClick = DateTime.MinValue;
        private Action _onRefresh;

        private static readonly Color BG_DARK = Color.FromArgb(10, 14, 23);
        private static readonly Color ACCENT_CYAN = Color.FromArgb(0, 212, 255);
        private static readonly Color GREEN = Color.FromArgb(0, 255, 136);
        private static readonly Color RED = Color.FromArgb(255, 68, 68);
        private static readonly Color AMBER = Color.FromArgb(255, 170, 0);
        private static readonly Color TEXT_LIGHT = Color.FromArgb(200, 208, 224);

        public MultiVehicleDashForm(Action onRefresh)
        {
            _onRefresh = onRefresh;
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "SWARM DASHBOARD — MISSION PLANNER";
            Size = new Size(1300, 800);
            MinimumSize = new Size(1000, 600);
            BackColor = BG_DARK;
            ForeColor = TEXT_LIGHT;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            var titlePanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 16, 28) };
            _titleLabel = new Label { Text = "◈  SWARM STATUS DASHBOARD", Font = new Font("Consolas", 14f, FontStyle.Bold), ForeColor = ACCENT_CYAN, AutoSize = true, Location = new Point(15, 14) };
            _timeLabel = new Label { Text = DateTime.Now.ToString("HH:mm:ss"), Font = new Font("Consolas", 12f), ForeColor = Color.FromArgb(100, 120, 160), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(Width - 130, 16) };
            titlePanel.Controls.Add(_titleLabel);
            titlePanel.Controls.Add(_timeLabel);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(12, 16, 28) };
            _statusLabel = new Label { Text = "READY", Font = new Font("Consolas", 10f), ForeColor = Color.FromArgb(100, 120, 160), AutoSize = true, Location = new Point(15, 20) };
            _globalArmBtn = CreateStyledButton("⚡ GLOBAL ARM ALL", RED, Color.FromArgb(60, 20, 20)); _globalArmBtn.Location = new Point(350, 10); _globalArmBtn.Click += GlobalArm_Click;
            _globalDisarmBtn = CreateStyledButton("■ GLOBAL DISARM ALL", GREEN, Color.FromArgb(20, 60, 20)); _globalDisarmBtn.Location = new Point(560, 10); _globalDisarmBtn.Click += GlobalDisarm_Click;
            var refreshBtn = CreateStyledButton("↻ REFRESH", ACCENT_CYAN, Color.FromArgb(15, 30, 50)); refreshBtn.Size = new Size(120, 38); refreshBtn.Location = new Point(770, 10);
            refreshBtn.Click += (s, ev) => { if (_onRefresh != null) _onRefresh(); _statusLabel.Text = "↻ MAPPING RESET..."; _statusLabel.ForeColor = AMBER; };
            bottomPanel.Controls.AddRange(new Control[] { _statusLabel, _globalArmBtn, _globalDisarmBtn, refreshBtn });

            _gridPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = BG_DARK, Padding = new Padding(10) };
            _gridPanel.ColumnCount = 5;
            _gridPanel.RowCount = 2;
            for (int i = 0; i < 5; i++) _gridPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            for (int i = 0; i < 2; i++) _gridPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            for (int i = 0; i < 10; i++)
            {
                _panels[i] = new DronePanel(i + 1) { Dock = DockStyle.Fill, Margin = new Padding(5) };
                _gridPanel.Controls.Add(_panels[i], i % 5, i / 5);
            }

            Controls.Add(_gridPanel);
            Controls.Add(bottomPanel);
            Controls.Add(titlePanel);

            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _clockTimer.Tick += (s, e) => _timeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockTimer.Start();
        }

        private Button CreateStyledButton(string text, Color accentColor, Color bgColor)
        {
            var btn = new Button { Text = text, Font = new Font("Consolas", 9f, FontStyle.Bold), ForeColor = accentColor, BackColor = bgColor, FlatStyle = FlatStyle.Flat, Size = new Size(190, 38), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderColor = accentColor; btn.FlatAppearance.BorderSize = 1; return btn;
        }

        private void GlobalArm_Click(object sender, EventArgs e)
        {
            if ((DateTime.Now - _lastArmClick).TotalMilliseconds < 800) _armClickCount++; else _armClickCount = 1;
            _lastArmClick = DateTime.Now;
            if (_armClickCount < 2) { _statusLabel.Text = "⚠ DOUBLE-CLICK TO CONFIRM ARM ALL"; _statusLabel.ForeColor = AMBER; return; }
            _armClickCount = 0;
            if (MessageBox.Show("⚠ WARNING: This will ARM ALL connected vehicles!\n\nAre you absolutely sure?", "CONFIRM GLOBAL ARM", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                foreach (var p in _panels)
                {
                    if (p != null && p.Data != null && p.Data.ComPort != null && p.Data.IsConnected)
                    {
                        try 
                        { 
                            var oldSys = p.Data.ComPort.sysidcurrent;
                            var oldComp = p.Data.ComPort.compidcurrent;
                            p.Data.ComPort.sysidcurrent = p.Data.SysId;
                            p.Data.ComPort.compidcurrent = p.Data.CompId;
                            p.Data.ComPort.doARM(true); 
                            p.Data.ComPort.sysidcurrent = oldSys;
                            p.Data.ComPort.compidcurrent = oldComp;
                        } 
                        catch { }
                    }
                }
                _statusLabel.Text = "✓ ARM command sent to all vehicles"; _statusLabel.ForeColor = GREEN;
            }
        }

        private void GlobalDisarm_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Disarm ALL connected vehicles?", "CONFIRM GLOBAL DISARM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                foreach (var p in _panels)
                {
                    if (p != null && p.Data != null && p.Data.ComPort != null && p.Data.IsConnected)
                    {
                        try 
                        { 
                            var oldSys = p.Data.ComPort.sysidcurrent;
                            var oldComp = p.Data.ComPort.compidcurrent;
                            p.Data.ComPort.sysidcurrent = p.Data.SysId;
                            p.Data.ComPort.compidcurrent = p.Data.CompId;
                            p.Data.ComPort.doARM(false); 
                            p.Data.ComPort.sysidcurrent = oldSys;
                            p.Data.ComPort.compidcurrent = oldComp;
                        } 
                        catch { }
                    }
                }
                _statusLabel.Text = "✓ DISARM command sent to all vehicles"; _statusLabel.ForeColor = GREEN;
            }
        }

        public void UpdateVehicles(VehicleData[] vehicles)
        {
            int connectedCount = 0;
            foreach (var v in vehicles) if (v != null && v.IsConnected) connectedCount++;

            if (!_statusLabel.Text.Contains("REFRESH") && !_statusLabel.Text.Contains("ARM"))
            {
                _statusLabel.Text = string.Format("{0} VEHICLE{1} CONNECTED", connectedCount, connectedCount != 1 ? "S" : "");
                _statusLabel.ForeColor = connectedCount > 0 ? ACCENT_CYAN : Color.FromArgb(100, 120, 160);
            }
            for (int i = 0; i < 10; i++)
            {
                _panels[i].UpdateData(vehicles[i]);
            }
        }
    }

    public class DronePanel : UserControl
    {
        private VehicleData _data;
        public VehicleData Data { get { return _data; } }

        private int _droneNumber;
        private ArtificialHorizon _horizon;
        private Button _armToggleBtn, _rtlBtn, _loiterBtn, _landBtn;

        private static readonly Color BG = Color.FromArgb(15, 20, 32);
        private static readonly Color BORDER = Color.FromArgb(26, 32, 53);
        private static readonly Color CYAN = Color.FromArgb(0, 212, 255);
        private static readonly Color GREEN = Color.FromArgb(0, 255, 136);
        private static readonly Color RED = Color.FromArgb(255, 68, 68);
        private static readonly Color AMBER = Color.FromArgb(255, 170, 0);
        private static readonly Color DIM = Color.FromArgb(80, 100, 140);

        public DronePanel(int droneNumber)
        {
            _droneNumber = droneNumber;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Default;

            _horizon = new ArtificialHorizon { Size = new Size(50, 50), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            
            _armToggleBtn = CreateButton("ARM", Color.FromArgb(50, 15, 15), RED);
            _armToggleBtn.Click += ArmToggleBtn_Click;

            _rtlBtn = CreateButton("RTL", Color.FromArgb(20, 30, 50), CYAN);
            _rtlBtn.Click += (s, e) => SetMode("RTL");

            _loiterBtn = CreateButton("LOITER", Color.FromArgb(20, 30, 50), CYAN);
            _loiterBtn.Click += (s, e) => SetMode("LOITER");

            _landBtn = CreateButton("LAND", Color.FromArgb(20, 30, 50), CYAN);
            _landBtn.Click += (s, e) => SetMode("LAND");

            Controls.AddRange(new Control[] { _horizon, _armToggleBtn, _rtlBtn, _loiterBtn, _landBtn });
        }

        private Button CreateButton(string text, Color bg, Color fg)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Consolas", 8f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(40, 55, 80);
            return btn;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _horizon.Location = new Point(Width - 55, 30);

            int btnH = 26;
            int y = Height - btnH - 6;
            int p = 4;
            int avail = Width - (p * 5);
            int wArm = (int)(avail * 0.30);
            int wR = (int)(avail * 0.22);
            int wLo = (int)(avail * 0.26);
            int wLa = avail - wArm - wR - wLo;

            int x = p;
            _armToggleBtn.Bounds = new Rectangle(x, y, wArm, btnH); x += wArm + p;
            _rtlBtn.Bounds = new Rectangle(x, y, wR, btnH); x += wR + p;
            _loiterBtn.Bounds = new Rectangle(x, y, wLo, btnH); x += wLo + p;
            _landBtn.Bounds = new Rectangle(x, y, wLa, btnH);
        }

        private void ArmToggleBtn_Click(object sender, EventArgs e)
        {
            if (_data == null || !_data.IsConnected) return;
            bool isArmed = _data.Armed;
            string action = isArmed ? "DISARM" : "ARM";
            
            if (MessageBox.Show(string.Format("Are you sure you want to {0} DRONE {1:D2}?", action, _droneNumber), string.Format("Confirm {0}", action), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try 
                { 
                    var oldSys = _data.ComPort.sysidcurrent;
                    var oldComp = _data.ComPort.compidcurrent;
                    _data.ComPort.sysidcurrent = _data.SysId;
                    _data.ComPort.compidcurrent = _data.CompId;
                    _data.ComPort.doARM(!isArmed); 
                    _data.ComPort.sysidcurrent = oldSys;
                    _data.ComPort.compidcurrent = oldComp;
                } 
                catch { }
            }
        }

        private void SetMode(string mode)
        {
            if (_data == null || !_data.IsConnected) return;
            try 
            { 
                var oldSys = _data.ComPort.sysidcurrent;
                var oldComp = _data.ComPort.compidcurrent;
                _data.ComPort.sysidcurrent = _data.SysId;
                _data.ComPort.compidcurrent = _data.CompId;
                _data.ComPort.setMode(mode); 
                _data.ComPort.sysidcurrent = oldSys;
                _data.ComPort.compidcurrent = oldComp;
            } 
            catch { }
        }

        public void UpdateData(VehicleData data)
        {
            _data = data;
            bool conn = (_data != null && _data.IsConnected);

            if (conn)
            {
                _horizon.SetAttitude(data.Roll, data.Pitch);
                if (_data.Armed)
                {
                    _armToggleBtn.Text = "ARMED"; _armToggleBtn.BackColor = Color.FromArgb(15, 50, 15); _armToggleBtn.ForeColor = GREEN; _armToggleBtn.FlatAppearance.BorderColor = GREEN;
                }
                else
                {
                    _armToggleBtn.Text = "ARM"; _armToggleBtn.BackColor = Color.FromArgb(50, 15, 15); _armToggleBtn.ForeColor = RED; _armToggleBtn.FlatAppearance.BorderColor = RED;
                }
                _armToggleBtn.Enabled = _rtlBtn.Enabled = _loiterBtn.Enabled = _landBtn.Enabled = true;
            }
            else
            {
                _armToggleBtn.Text = "ARM"; _armToggleBtn.BackColor = Color.FromArgb(25, 25, 30); _armToggleBtn.ForeColor = DIM; _armToggleBtn.FlatAppearance.BorderColor = Color.FromArgb(40, 50, 70);
                _armToggleBtn.Enabled = _rtlBtn.Enabled = _loiterBtn.Enabled = _landBtn.Enabled = false;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bgBrush = new SolidBrush(BG)) g.FillRectangle(bgBrush, ClientRectangle);
            
            bool conn = (_data != null && _data.IsConnected);
            var borderCol = conn ? (_data.Armed ? Color.FromArgb(0, 100, 60) : BORDER) : Color.FromArgb(40, 15, 15);
            using (var pen = new Pen(borderCol, conn && _data.Armed ? 2f : 1f)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            var hdrRect = new Rectangle(0, 0, Width, 26);
            var hdrColor = conn ? (_data.Armed ? Color.FromArgb(0, 50, 30) : Color.FromArgb(20, 25, 40)) : Color.FromArgb(40, 15, 15);
            using (var hdrBrush = new SolidBrush(hdrColor)) g.FillRectangle(hdrBrush, hdrRect);

            var headerFont = new Font("Consolas", 10f, FontStyle.Bold);
            using (var b = new SolidBrush(Color.White)) g.DrawString(string.Format("DRONE {0:D2}", _droneNumber), headerFont, b, 5, 4);

            var smallFont = new Font("Consolas", 8f);
            var ledColor = conn ? GREEN : RED;
            string portText = conn ? (_data.PortName + " | SYS:" + _data.SysId) : "DISCONNECTED";
            
            var sz = g.MeasureString(portText, smallFont);
            using (var b = new SolidBrush(ledColor))
            {
                g.DrawString(portText, smallFont, b, Width - sz.Width - 18, 6);
                g.FillEllipse(b, Width - 14, 8, 8, 8);
                if (conn) { using (var gl = new SolidBrush(Color.FromArgb(60, ledColor))) g.FillEllipse(gl, Width - 17, 5, 14, 14); }
            }

            using (var pen = new Pen(Color.FromArgb(40, 55, 80), 1f)) g.DrawLine(pen, 0, 26, Width, 26);

            var dataFont = new Font("Consolas", 10.5f, FontStyle.Bold);
            int y = 35; int x = 8; int lh = (int)(dataFont.GetHeight(g) + 5);

            if (conn)
            {
                DrawData(g, dataFont, "BAT: ", string.Format("{0:F1} V", _data.BatteryVoltage), x, y, CYAN); y += lh;
                DrawData(g, dataFont, "SAT: ", string.Format("{0}", _data.SatCount), x, y, CYAN); y += lh;
                DrawData(g, dataFont, "SPD: ", string.Format("{0:F1} m/s", _data.Groundspeed), x, y, CYAN); y += lh;
                DrawData(g, dataFont, "ALT: ", string.Format("{0:F1} m", _data.Altitude), x, y, CYAN); y += lh;
                DrawData(g, dataFont, "MOD: ", _data.Mode, x, y, AMBER);
            }
            else
            {
                using (var b = new SolidBrush(DIM)) g.DrawString("NO TELEMETRY", dataFont, b, x, y);
                if (_data != null)
                {
                    y += lh;
                    using (var b = new SolidBrush(Color.FromArgb(60, 70, 90)))
                        g.DrawString(string.Format("LAST KNOWN SYSID: {0}", _data.SysId), new Font("Consolas", 8.5f), b, x, y);
                }
            }
            
            headerFont.Dispose(); smallFont.Dispose(); dataFont.Dispose();
        }

        private void DrawData(Graphics g, Font font, string label, string value, int x, int y, Color valColor)
        {
            using (var lb = new SolidBrush(DIM)) g.DrawString(label, font, lb, x, y);
            int vx = x + (int)g.MeasureString(label, font).Width - 2;
            using (var vb = new SolidBrush(valColor)) g.DrawString(value, font, vb, vx, y);
        }
    }

    public class ArtificialHorizon : Control
    {
        private float _roll = 0, _pitch = 0;
        private static readonly Color SKY = Color.FromArgb(30, 80, 160);
        private static readonly Color GROUND = Color.FromArgb(120, 70, 20);

        public ArtificialHorizon() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        public void SetAttitude(float roll, float pitch) { _roll = roll; _pitch = pitch; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width / 2, cy = Height / 2, radius = Math.Min(cx, cy) - 2;
            using (var clipPath = new GraphicsPath())
            {
                clipPath.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2); g.SetClip(clipPath);
                g.TranslateTransform(cx, cy); g.RotateTransform(-_roll);
                float pPix = _pitch * (radius / 30f);
                using (var sky = new SolidBrush(SKY)) g.FillRectangle(sky, -radius * 2, -radius * 2 + pPix, radius * 4, radius * 2);
                using (var gnd = new SolidBrush(GROUND)) g.FillRectangle(gnd, -radius * 2, pPix, radius * 4, radius * 2);
                using (var pen = new Pen(Color.White, 1.5f)) g.DrawLine(pen, -radius * 2, pPix, radius * 2, pPix);
                g.ResetTransform(); g.ResetClip();
            }
            using (var pen = new Pen(Color.FromArgb(60, 80, 120), 1.5f)) g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            using (var pen = new Pen(Color.FromArgb(255, 200, 0), 1.5f))
            {
                g.DrawLine(pen, cx - 6, cy, cx - 2, cy); g.DrawLine(pen, cx + 2, cy, cx + 6, cy); g.DrawLine(pen, cx, cy - 2, cx, cy + 2);
            }
        }
    }
}