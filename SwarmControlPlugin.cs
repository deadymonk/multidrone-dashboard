using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MissionPlanner;
using MissionPlanner.Utilities;
using MissionPlanner.Controls;

namespace SwarmControl
{
    public class SwarmControlPlugin : MissionPlanner.Plugin.Plugin
    {
        private SwarmControlForm _form;

        public override string Name { get { return "Swarm Command Orchestrator"; } }
        public override string Version { get { return "2.2"; } }
        public override string Author { get { return "ZeroGravity"; } }

        public override bool Init()
        {
            loopratehz = 5;
            return true;
        }

        public override bool Loaded()
        {
            try 
            {
                var menuItem = new ToolStripMenuItem("Swarm Orchestrator");
                menuItem.Click += (s, e) => ShowForm();
                Host.FDMenuMap.Items.Add(menuItem);
                
                // We will NOT auto-open to avoid conflicts with MP startup
                // Host.MainForm.BeginInvoke((MethodInvoker)(() => ShowForm()));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading SwarmControlPlugin: " + ex.Message);
            }
            return true;
        }

        private void ShowForm()
        {
            try
            {
                if (_form == null || _form.IsDisposed)
                {
                    _form = new SwarmControlForm();
                    _form.Show();
                }
                else
                {
                    _form.BringToFront();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening Swarm Orchestrator Form: " + ex.ToString());
            }
        }

        public override bool Loop()
        {
            if (_form != null && !_form.IsDisposed)
            {
                _form.OnLoop();
            }
            return true;
        }

        public override bool Exit()
        {
            if (_form != null && !_form.IsDisposed)
                _form.Close();
            return true;
        }
    }

    public enum SwarmState
    {
        IDLE,
        FORMING_INNER,
        FORMING_OUTER,
        FORMED,
        EXPANDING_OUTER,
        EXPANDING_INNER,
        CONTRACTING_INNER,
        CONTRACTING_OUTER
    }

    public class SwarmVehicle
    {
        public MAVLinkInterface Port;
        public MAVLink.MAVState Mav;
        public byte SysId { get { return Mav.sysid; } }
        public byte CompId { get { return Mav.compid; } }
        public CurrentState CS { get { return Mav.cs; } }
    }

    public class SwarmControlForm : Form
    {
        private ComboBox _leaderCombo, _formationCombo;
        private TextBox _baseAltInput;
        private Button _takeoffBtn, _formSwarmBtn, _splitBtn;
        private TrackBar _spacingSlider, _rotationSlider;
        private Label _spacingLbl, _rotationLbl, _statusLbl;
        private Button _armAllBtn, _disarmAllBtn, _rtlAllBtn;

        private SwarmState _currentState = SwarmState.IDLE;
        private SwarmVehicle _leader = null;
        private List<SwarmVehicle> _followers = new List<SwarmVehicle>();
        private Dictionary<SwarmVehicle, GridSlot> _followerSlots = new Dictionary<SwarmVehicle, GridSlot>();
        private Dictionary<SwarmVehicle, TargetPos> _followerTargets = new Dictionary<SwarmVehicle, TargetPos>();

        private int _baseAlt = 5;
        private int _currentSpacing = 5;
        private int _currentRotation = 0;
        private int _targetSpacing = 5;
        
        private float _vx = 0;
        private float _vy = 0;
        private bool _isMovingLeader = false;

        private class GridSlot { public int X, Y, Layer; }
        private class TargetPos { public double Lat, Lng; public float Alt; }

        private readonly List<GridSlot> _gridTemplate = new List<GridSlot>
        {
            new GridSlot{X=0, Y=1, Layer=1},     // N
            new GridSlot{X=0, Y=-1, Layer=1},    // S
            new GridSlot{X=1, Y=0, Layer=1},     // E
            new GridSlot{X=-1, Y=0, Layer=1},    // W
            new GridSlot{X=1, Y=1, Layer=2},     // NE
            new GridSlot{X=-1, Y=1, Layer=2},    // NW
            new GridSlot{X=1, Y=-1, Layer=2},    // SE
            new GridSlot{X=-1, Y=-1, Layer=2}    // SW
        };

        private readonly List<GridSlot> _lineTemplate = new List<GridSlot>
        {
            new GridSlot{X=1, Y=0, Layer=1},
            new GridSlot{X=2, Y=0, Layer=2},
            new GridSlot{X=3, Y=0, Layer=3},
            new GridSlot{X=4, Y=0, Layer=4},
            new GridSlot{X=5, Y=0, Layer=5},
            new GridSlot{X=6, Y=0, Layer=6},
            new GridSlot{X=7, Y=0, Layer=7},
            new GridSlot{X=8, Y=0, Layer=8}
        };

        public SwarmControlForm()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "SWARM COMMAND ORCHESTRATOR — MISSION PLANNER";
            Size = new Size(720, 650);
            BackColor = Color.FromArgb(18, 22, 36);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            KeyPreview = true;
            KeyDown += SwarmControlForm_KeyDown;
            KeyUp += SwarmControlForm_KeyUp;

            var font = new Font("Consolas", 10f);
            var btnFont = new Font("Consolas", 9f, FontStyle.Bold);

            // -- Leader Selection --
            var leaderLbl = new Label { Text = "Leader Drone:", Location = new Point(20, 25), AutoSize = true, Font = font, ForeColor = Color.Cyan };
            _leaderCombo = new ComboBox { Location = new Point(150, 20), Size = new Size(150, 25), Font = font, DropDownStyle = ComboBoxStyle.DropDownList };
            _leaderCombo.DropDown += (s, e) => PopulateDrones();
            
            var assignBtn = new Button { Text = "INITIATE SWARM", Location = new Point(320, 18), Size = new Size(150, 30), BackColor = Color.FromArgb(20, 50, 20), FlatStyle = FlatStyle.Flat, Font = btnFont };
            assignBtn.Click += AssignBtn_Click;
            
            _splitBtn = new Button { Text = "SPLIT SWARM", Location = new Point(480, 18), Size = new Size(150, 30), BackColor = Color.FromArgb(50, 20, 20), FlatStyle = FlatStyle.Flat, Font = btnFont };
            _splitBtn.Click += (s, e) => { _currentState = SwarmState.IDLE; _leader = null; _followers.Clear(); SetStatus("SWARM SPLIT / DISCONNECTED", Color.Red); };

            // -- Phase 1: Takeoff --
            var p1Box = new GroupBox { Text = "1. TAKEOFF", Location = new Point(20, 70), Size = new Size(660, 80), ForeColor = Color.White, Font = btnFont };
            var altLbl = new Label { Text = "Base Altitude (m):", Location = new Point(15, 35), AutoSize = true, Font = font };
            _baseAltInput = new TextBox { Text = "5", Location = new Point(170, 32), Size = new Size(50, 25), Font = font };
            _takeoffBtn = new Button { Text = "SIMULTANEOUS TAKEOFF (Leader +5m)", Location = new Point(240, 28), Size = new Size(400, 35), BackColor = Color.FromArgb(50, 20, 20), FlatStyle = FlatStyle.Flat };
            _takeoffBtn.Click += TakeoffBtn_Click;
            p1Box.Controls.AddRange(new Control[] { altLbl, _baseAltInput, _takeoffBtn });

            // -- Phase 2: Formation --
            var p2Box = new GroupBox { Text = "2. FORMATION ASSEMBLY", Location = new Point(20, 160), Size = new Size(660, 80), ForeColor = Color.White, Font = btnFont };
            var formLbl = new Label { Text = "Pattern:", Location = new Point(15, 35), AutoSize = true, Font = font };
            _formationCombo = new ComboBox { Location = new Point(90, 32), Size = new Size(130, 25), Font = font, DropDownStyle = ComboBoxStyle.DropDownList };
            _formationCombo.Items.AddRange(new object[] { "3x3 Grid", "Straight Line" });
            _formationCombo.SelectedIndex = 0;
            
            _formSwarmBtn = new Button { Text = "ASSEMBLE FORMATION", Location = new Point(240, 28), Size = new Size(400, 35), BackColor = Color.FromArgb(20, 30, 50), FlatStyle = FlatStyle.Flat };
            _formSwarmBtn.Click += FormSwarmBtn_Click;
            p2Box.Controls.AddRange(new Control[] { formLbl, _formationCombo, _formSwarmBtn });

            // -- Phase 3: Dynamic Controls --
            var p3Box = new GroupBox { Text = "3. DYNAMIC ADJUSTMENTS", Location = new Point(20, 250), Size = new Size(660, 120), ForeColor = Color.White, Font = btnFont };
            
            _spacingLbl = new Label { Text = "Spacing: 5m", Location = new Point(15, 30), AutoSize = true, Font = font };
            _spacingSlider = new TrackBar { Location = new Point(150, 25), Size = new Size(400, 45), Minimum = 2, Maximum = 20, Value = 5 };
            _spacingSlider.Scroll += (s, e) => { _spacingLbl.Text = string.Format("Spacing: {0}m", _spacingSlider.Value); };
            _spacingSlider.MouseUp += (s, e) => TriggerSpacingChange();

            _rotationLbl = new Label { Text = "Rotation: 0°", Location = new Point(15, 75), AutoSize = true, Font = font };
            _rotationSlider = new TrackBar { Location = new Point(150, 70), Size = new Size(400, 45), Minimum = -180, Maximum = 180, Value = 0, TickFrequency = 15 };
            _rotationSlider.Scroll += (s, e) => { _rotationLbl.Text = string.Format("Rotation: {0}°", _rotationSlider.Value); TriggerRotationChange(); };

            p3Box.Controls.AddRange(new Control[] { _spacingLbl, _spacingSlider, _rotationLbl, _rotationSlider });

            // -- Master Actions --
            var p4Box = new GroupBox { Text = "GLOBAL CONTROLS", Location = new Point(20, 380), Size = new Size(660, 80), ForeColor = Color.White, Font = btnFont };
            _armAllBtn = new Button { Text = "⚡ ARM ALL", Location = new Point(15, 28), Size = new Size(200, 35), BackColor = Color.FromArgb(60, 20, 20), FlatStyle = FlatStyle.Flat };
            _armAllBtn.Click += (s, e) => ExecuteGlobal(m => m.doARM(true));
            _disarmAllBtn = new Button { Text = "■ DISARM ALL", Location = new Point(230, 28), Size = new Size(200, 35), BackColor = Color.FromArgb(20, 60, 20), FlatStyle = FlatStyle.Flat };
            _disarmAllBtn.Click += (s, e) => ExecuteGlobal(m => m.doARM(false));
            _rtlAllBtn = new Button { Text = "🏠 STAGGERED RTL", Location = new Point(445, 28), Size = new Size(200, 35), BackColor = Color.FromArgb(20, 40, 60), FlatStyle = FlatStyle.Flat };
            _rtlAllBtn.Click += RtlAllBtn_Click;
            p4Box.Controls.AddRange(new Control[] { _armAllBtn, _disarmAllBtn, _rtlAllBtn });

            var helpLbl = new Label { Text = "Keyboard Controls: Use ARROW KEYS to move the Leader (and the Swarm).", Location = new Point(20, 480), AutoSize = true, Font = font, ForeColor = Color.Yellow };

            // -- Status Footer --
            _statusLbl = new Label { Text = "STATUS: IDLE", Location = new Point(20, 520), AutoSize = true, Font = new Font("Consolas", 12f, FontStyle.Bold), ForeColor = Color.Cyan };

            Controls.AddRange(new Control[] { leaderLbl, _leaderCombo, assignBtn, _splitBtn, p1Box, p2Box, p3Box, p4Box, helpLbl, _statusLbl });

            LockSliders(true);
        }

        private void ExecuteOnVehicle(byte sysid, byte compid, MAVLinkInterface port, Action<MAVLinkInterface> action)
        {
            if (port == null) return;
            try
            {
                var oldSys = port.sysidcurrent;
                var oldComp = port.compidcurrent;
                port.sysidcurrent = sysid;
                port.compidcurrent = compid;
                action(port);
                port.sysidcurrent = oldSys;
                port.compidcurrent = oldComp;
            }
            catch { }
        }

        private void ExecuteGlobal(Action<MAVLinkInterface> action)
        {
            foreach (var port in MainV2.Comports)
            {
                if (port == null || port.MAVlist == null) continue;
                foreach (var mav in port.MAVlist)
                {
                    if (mav == null) continue;
                    ExecuteOnVehicle(mav.sysid, mav.compid, port, action);
                }
            }
        }

        private void PopulateDrones()
        {
            var current = _leaderCombo.SelectedItem != null ? _leaderCombo.SelectedItem.ToString() : null;
            _leaderCombo.Items.Clear();
            foreach (var port in MainV2.Comports)
            {
                if (port == null || port.MAVlist == null) continue;
                foreach (var mav in port.MAVlist)
                {
                    if (mav != null)
                        _leaderCombo.Items.Add(string.Format("SYS-{0} ({1})", mav.sysid, port.BaseStream != null ? port.BaseStream.PortName : ""));
                }
            }
            if (current != null && _leaderCombo.Items.Contains(current))
                _leaderCombo.SelectedItem = current;
            else if (_leaderCombo.Items.Count > 0)
                _leaderCombo.SelectedIndex = 0;
        }

        private void AssignBtn_Click(object sender, EventArgs e)
        {
            _leader = null;
            _followers.Clear();
            _followerSlots.Clear();
            
            string sel = _leaderCombo.SelectedItem != null ? _leaderCombo.SelectedItem.ToString() : null;
            if (string.IsNullOrEmpty(sel)) return;

            foreach (var port in MainV2.Comports)
            {
                if (port == null || port.MAVlist == null) continue;
                foreach (var mav in port.MAVlist)
                {
                    if (mav == null) continue;
                    var veh = new SwarmVehicle { Port = port, Mav = mav };
                    if (string.Format("SYS-{0} ({1})", mav.sysid, port.BaseStream != null ? port.BaseStream.PortName : "") == sel)
                        _leader = veh;
                    else
                        _followers.Add(veh);
                }
            }

            SetStatus(string.Format("SWARM INITIATED | Leader: SYS-{0} | Followers: {1}", _leader != null ? (object)_leader.SysId : "NONE", _followers.Count), Color.Green);
        }

        private void TakeoffBtn_Click(object sender, EventArgs e)
        {
            if (_leader == null) { MessageBox.Show("Initiate swarm first."); return; }
            if (!int.TryParse(_baseAltInput.Text, out _baseAlt)) _baseAlt = 5;
            
            var res = MessageBox.Show(string.Format("ARM AND TAKEOFF ALL? Leader to {0}m, Followers to {1}m", _baseAlt + 5, _baseAlt), "Confirm Takeoff", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (res == DialogResult.Yes)
            {
                // Leader
                ExecuteOnVehicle(_leader.SysId, _leader.CompId, _leader.Port, m => {
                    m.setMode("GUIDED");
                    m.doARM(true);
                    m.doCommand(MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, _baseAlt + 5);
                });

                // Followers
                foreach (var f in _followers)
                {
                    ExecuteOnVehicle(f.SysId, f.CompId, f.Port, m => {
                        m.setMode("GUIDED");
                        m.doARM(true);
                        m.doCommand(MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, _baseAlt);
                    });
                }
                
                SetStatus(string.Format("TAKING OFF. L:{0}m, F:{1}m", _baseAlt + 5, _baseAlt), Color.Cyan);
            }
        }

        private void FormSwarmBtn_Click(object sender, EventArgs e)
        {
            if (_leader == null || _followers.Count == 0) { MessageBox.Show("Initiate swarm with followers first."); return; }
            
            // Assign slots based on selection
            var template = _formationCombo.SelectedItem.ToString() == "3x3 Grid" ? _gridTemplate : _lineTemplate;
            _followerSlots.Clear();
            for (int i = 0; i < _followers.Count && i < template.Count; i++)
            {
                _followerSlots[_followers[i]] = template[i];
            }

            _currentSpacing = _spacingSlider.Value;
            _currentRotation = _rotationSlider.Value;
            CalculateTargets();
            
            _currentState = SwarmState.FORMING_INNER;
            SetStatus("FORMING: INNER LAYER MOVING", Color.Yellow);
            DispatchLayer(1); // Layer 1 first
        }

        private void TriggerSpacingChange()
        {
            if (_currentState != SwarmState.FORMED) return;
            _targetSpacing = _spacingSlider.Value;
            
            if (_targetSpacing > _currentSpacing)
            {
                _currentState = SwarmState.EXPANDING_OUTER;
                CalculateTargetsForSpacing(_targetSpacing);
                SetStatus("EXPANDING: OUTER LAYER MOVING", Color.Yellow);
                DispatchLayer(2); // Outward first
            }
            else if (_targetSpacing < _currentSpacing)
            {
                _currentState = SwarmState.CONTRACTING_INNER;
                CalculateTargetsForSpacing(_targetSpacing);
                SetStatus("CONTRACTING: INNER LAYER MOVING", Color.Yellow);
                DispatchLayer(1); // Inward first
            }
        }

        private void TriggerRotationChange()
        {
            if (_currentState != SwarmState.FORMED) return;
            // Rotation is always treated like expansion (outward-inward for safety)
            _currentState = SwarmState.EXPANDING_OUTER;
            _currentRotation = _rotationSlider.Value;
            CalculateTargets();
            SetStatus("ROTATING: OUTER LAYER MOVING", Color.Yellow);
            DispatchLayer(2);
        }

        private void LockSliders(bool lockem)
        {
            _spacingSlider.Enabled = !lockem;
            _rotationSlider.Enabled = !lockem;
        }

        private void SetStatus(string msg, Color? col = null)
        {
            if (_statusLbl.InvokeRequired)
            {
                _statusLbl.BeginInvoke((MethodInvoker)(() => SetStatus(msg, col)));
                return;
            }
            _statusLbl.Text = "STATUS: " + msg;
            if (col.HasValue) _statusLbl.ForeColor = col.Value;
        }

        private void CalculateTargetsForSpacing(int spacing)
        {
            _currentSpacing = spacing;
            CalculateTargets();
        }

        private void CalculateTargets()
        {
            if (_leader == null || _leader.CS == null) return;
            
            double leadLat = _leader.CS.lat;
            double leadLng = _leader.CS.lng;
            float targetAlt = _baseAlt; 
            
            double rotRad = _currentRotation * Math.PI / 180.0;
            double r_earth = 6378137.0;

            foreach (var f in _followers)
            {
                if (!_followerSlots.ContainsKey(f)) continue;
                var slot = _followerSlots[f];

                double ox = slot.X * _currentSpacing;
                double oy = slot.Y * _currentSpacing;

                double dEast = ox * Math.Cos(rotRad) - oy * Math.Sin(rotRad);
                double dNorth = ox * Math.Sin(rotRad) + oy * Math.Cos(rotRad);

                double newLat = leadLat + (dNorth / r_earth) * (180.0 / Math.PI);
                double newLng = leadLng + (dEast / (r_earth * Math.Cos(leadLat * Math.PI / 180.0))) * (180.0 / Math.PI);

                _followerTargets[f] = new TargetPos { Lat = newLat, Lng = newLng, Alt = targetAlt };
            }
        }

        private void DispatchLayer(int layer)
        {
            foreach (var f in _followers)
            {
                if (_followerSlots.ContainsKey(f) && _followerSlots[f].Layer == layer)
                {
                    if (_followerTargets.ContainsKey(f))
                    {
                        var t = _followerTargets[f];
                        ExecuteOnVehicle(f.SysId, f.CompId, f.Port, m => {
                            m.setMode("GUIDED");
                            
                            MAVLink.mavlink_set_position_target_global_int_t req = new MAVLink.mavlink_set_position_target_global_int_t();
                            req.target_system = f.SysId;
                            req.target_component = f.CompId;
                            req.coordinate_frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT_INT;
                            // Ignore Yaw: 0b110111111000 = 3576
                            req.type_mask = 3576; 
                            req.lat_int = (int)(t.Lat * 1e7);
                            req.lon_int = (int)(t.Lng * 1e7);
                            req.alt = t.Alt;
                            m.generatePacket((byte)MAVLink.MAVLINK_MSG_ID.SET_POSITION_TARGET_GLOBAL_INT, req);
                        });
                    }
                }
            }
        }

        private bool CheckLayerArrived(int layer)
        {
            bool allArrived = true;
            bool foundAny = false;
            foreach (var f in _followers)
            {
                if (_followerSlots.ContainsKey(f) && _followerSlots[f].Layer == layer)
                {
                    foundAny = true;
                    if (_followerTargets.ContainsKey(f) && f.CS != null)
                    {
                        var t = _followerTargets[f];
                        double dist = GetDistance(f.CS.lat, f.CS.lng, t.Lat, t.Lng);
                        if (dist > 1.5) allArrived = false;
                    }
                }
            }
            if (!foundAny) return true; // If no drones in this layer, treat as arrived
            return allArrived;
        }

        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6378137;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private void RtlAllBtn_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Start staggered RTL sequence? (1 second delay between drones)", "Confirm RTL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                SetStatus("INITIATING STAGGERED RTL...", Color.Red);
                Task.Run(() => {
                    if (_leader != null)
                    {
                        ExecuteOnVehicle(_leader.SysId, _leader.CompId, _leader.Port, m => m.setMode("RTL"));
                        Thread.Sleep(1000);
                    }
                    foreach (var f in _followers)
                    {
                        ExecuteOnVehicle(f.SysId, f.CompId, f.Port, m => m.setMode("RTL"));
                        Thread.Sleep(1000);
                    }
                    SetStatus("STAGGERED RTL INITIATED", Color.Cyan);
                });
            }
        }

        private void SwarmControlForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_leader == null || _currentState != SwarmState.FORMED) return;
            
            float speed = 3.0f; // 3 m/s
            if (e.KeyCode == Keys.Up) { _vx = speed; _vy = 0; }
            else if (e.KeyCode == Keys.Down) { _vx = -speed; _vy = 0; }
            else if (e.KeyCode == Keys.Left) { _vx = 0; _vy = -speed; }
            else if (e.KeyCode == Keys.Right) { _vx = 0; _vy = speed; }
            else return;

            _isMovingLeader = true;
            e.Handled = true;
        }

        private void SwarmControlForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                _vx = 0;
                _vy = 0;
                _isMovingLeader = false;
                
                if (_leader != null && _leader.CS != null)
                {
                    ExecuteOnVehicle(_leader.SysId, _leader.CompId, _leader.Port, m => {
                        MAVLink.mavlink_set_position_target_global_int_t req = new MAVLink.mavlink_set_position_target_global_int_t();
                        req.target_system = _leader.SysId;
                        req.target_component = _leader.CompId;
                        req.coordinate_frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT_INT;
                        req.type_mask = 3576; // Ignore yaw, use pos
                        req.lat_int = (int)(_leader.CS.lat * 1e7);
                        req.lon_int = (int)(_leader.CS.lng * 1e7);
                        req.alt = _baseAlt + 5;
                        m.generatePacket((byte)MAVLink.MAVLINK_MSG_ID.SET_POSITION_TARGET_GLOBAL_INT, req);
                    });
                }
            }
        }

        public void OnLoop()
        {
            if (_isMovingLeader && _leader != null)
            {
                ExecuteOnVehicle(_leader.SysId, _leader.CompId, _leader.Port, m => {
                    MAVLink.mavlink_set_position_target_global_int_t req = new MAVLink.mavlink_set_position_target_global_int_t();
                    req.target_system = _leader.SysId;
                    req.target_component = _leader.CompId;
                    req.coordinate_frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT_INT;
                    req.type_mask = 1991; // Ignore pos, ignore accel, ignore yaw. Use VELOCITY.
                    req.vx = _vx;
                    req.vy = _vy;
                    req.vz = 0;
                    m.generatePacket((byte)MAVLink.MAVLINK_MSG_ID.SET_POSITION_TARGET_GLOBAL_INT, req);
                });
            }

            if (_currentState == SwarmState.IDLE) return;

            if (_currentState == SwarmState.FORMED)
            {
                if (_leader != null && _leader.CS != null)
                {
                    CalculateTargets();
                    DispatchLayer(1);
                    DispatchLayer(2);
                    DispatchLayer(3); DispatchLayer(4); DispatchLayer(5); DispatchLayer(6); DispatchLayer(7); DispatchLayer(8);
                }
                return;
            }

            if (_currentState == SwarmState.FORMING_INNER)
            {
                if (CheckLayerArrived(1)) { _currentState = SwarmState.FORMING_OUTER; DispatchLayer(2); SetStatus("FORMING: OUTER LAYER MOVING", Color.Yellow); }
            }
            else if (_currentState == SwarmState.FORMING_OUTER)
            {
                if (CheckLayerArrived(2)) { _currentState = SwarmState.FORMED; BeginInvoke((MethodInvoker)(() => LockSliders(false))); SetStatus("SWARM FORMED - READY", Color.Green); }
            }
            else if (_currentState == SwarmState.EXPANDING_OUTER)
            {
                if (CheckLayerArrived(2)) { _currentState = SwarmState.EXPANDING_INNER; DispatchLayer(1); SetStatus("ADJUSTING: INNER LAYER MOVING", Color.Yellow); }
            }
            else if (_currentState == SwarmState.EXPANDING_INNER)
            {
                if (CheckLayerArrived(1)) { _currentState = SwarmState.FORMED; SetStatus("SWARM FORMED - READY", Color.Green); }
            }
            else if (_currentState == SwarmState.CONTRACTING_INNER)
            {
                if (CheckLayerArrived(1)) { _currentState = SwarmState.CONTRACTING_OUTER; DispatchLayer(2); SetStatus("CONTRACTING: OUTER LAYER MOVING", Color.Yellow); }
            }
            else if (_currentState == SwarmState.CONTRACTING_OUTER)
            {
                if (CheckLayerArrived(2)) { _currentState = SwarmState.FORMED; SetStatus("SWARM FORMED - READY", Color.Green); }
            }
            
            if (_formationCombo.SelectedItem != null && _formationCombo.SelectedItem.ToString() == "Straight Line")
            {
                if (_currentState == SwarmState.FORMING_INNER)
                {
                    DispatchLayer(2); DispatchLayer(3); DispatchLayer(4); DispatchLayer(5); DispatchLayer(6); DispatchLayer(7); DispatchLayer(8);
                    bool allLine = CheckLayerArrived(1) && CheckLayerArrived(2) && CheckLayerArrived(3) && CheckLayerArrived(4) && CheckLayerArrived(5) && CheckLayerArrived(6) && CheckLayerArrived(7) && CheckLineArrived();
                    if (allLine) { _currentState = SwarmState.FORMED; BeginInvoke((MethodInvoker)(() => LockSliders(false))); SetStatus("SWARM FORMED - READY", Color.Green); }
                }
            }
        }
        
        private bool CheckLineArrived()
        {
             return CheckLayerArrived(8);
        }
    }
}
