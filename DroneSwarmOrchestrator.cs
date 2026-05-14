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

namespace DroneSwarm
{
    public class DroneSwarmOrchestrator : MissionPlanner.Plugin.Plugin
    {
        private SwarmForm _form;

        public override string Name { get { return "Drone Swarm Orchestrator"; } }
        public override string Version { get { return "1.0"; } }
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
            }
            catch (Exception ex)
            {
                MessageBox.Show("Swarm Plugin Error: " + ex.Message);
            }
            return true;
        }

        private void ShowForm()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new SwarmForm();
                _form.Show();
            }
            else
            {
                _form.BringToFront();
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
    }

    public class SwarmVehicle
    {
        public MAVLinkInterface Port;
        public MAVLink.MAVState Mav;
        public byte SysId { get { return Mav.sysid; } }
        public byte CompId { get { return Mav.compid; } }
        public CurrentState CS { get { return Mav.cs; } }
    }

    public class SwarmForm : Form
    {
        private ComboBox _leaderCombo;
        private Label _statusLbl;
        private SwarmVehicle _leader = null;
        private List<SwarmVehicle> _followers = new List<SwarmVehicle>();
        private int _baseAlt = 5;

        public SwarmForm()
        {
            Text = "DRONE SWARM ORCHESTRATOR";
            Size = new Size(500, 450);
            BackColor = Color.FromArgb(20, 24, 40);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var font = new Font("Consolas", 10f);

            var lbl = new Label { Text = "Leader Drone:", Location = new Point(20, 30), AutoSize = true, Font = font };
            _leaderCombo = new ComboBox { Location = new Point(140, 27), Size = new Size(180, 25), Font = font, DropDownStyle = ComboBoxStyle.DropDownList };
            _leaderCombo.DropDown += (s, e) => PopulateDrones();

            var initBtn = new Button { Text = "INITIATE SWARM", Location = new Point(330, 25), Size = new Size(140, 30), BackColor = Color.DarkGreen, FlatStyle = FlatStyle.Flat, Font = font };
            initBtn.Click += (s, e) => AssignSwarm();

            var takeoffBtn = new Button { Text = "SIMULTANEOUS TAKEOFF", Location = new Point(20, 80), Size = new Size(450, 45), BackColor = Color.DarkRed, FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 11f, FontStyle.Bold) };
            takeoffBtn.Click += (s, e) => TakeoffAll();

            var rtlBtn = new Button { Text = "GLOBAL RTL", Location = new Point(20, 140), Size = new Size(450, 40), BackColor = Color.DarkBlue, FlatStyle = FlatStyle.Flat, Font = font };
            rtlBtn.Click += (s, e) => ExecuteGlobal(m => m.setMode("RTL"));

            _statusLbl = new Label { Text = "STATUS: READY", Location = new Point(20, 350), AutoSize = true, Font = new Font("Consolas", 12f, FontStyle.Bold), ForeColor = Color.Cyan };

            Controls.AddRange(new Control[] { lbl, _leaderCombo, initBtn, takeoffBtn, rtlBtn, _statusLbl });
        }

        private void PopulateDrones()
        {
            _leaderCombo.Items.Clear();
            foreach (var port in MainV2.Comports)
            {
                if (port == null || port.MAVlist == null) continue;
                foreach (var mav in port.MAVlist)
                {
                    if (mav != null)
                        _leaderCombo.Items.Add(string.Format("SYS-{0} ({1})", mav.sysid, port.BaseStream?.PortName ?? ""));
                }
            }
        }

        private void AssignSwarm()
        {
            _leader = null;
            _followers.Clear();
            string sel = _leaderCombo.SelectedItem?.ToString();
            
            foreach (var port in MainV2.Comports)
            {
                if (port == null || port.MAVlist == null) continue;
                foreach (var mav in port.MAVlist)
                {
                    if (mav == null) continue;
                    var veh = new SwarmVehicle { Port = port, Mav = mav };
                    if (string.Format("SYS-{0} ({1})", mav.sysid, port.BaseStream?.PortName ?? "") == sel)
                        _leader = veh;
                    else
                        _followers.Add(veh);
                }
            }
            _statusLbl.Text = "STATUS: " + _followers.Count + " FOLLOWERS READY";
            _statusLbl.ForeColor = Color.Green;
        }

        private void TakeoffAll()
        {
            if (_leader == null) { MessageBox.Show("Please assign a leader first!"); return; }
            
            // Leader takes off slightly higher (+5m) than followers as requested
            ExecuteOnVehicle(_leader.SysId, _leader.CompId, _leader.Port, m => {
                m.setMode("GUIDED");
                m.doARM(true);
                m.doCommand(MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, _baseAlt + 5);
            });

            foreach (var f in _followers)
            {
                ExecuteOnVehicle(f.SysId, f.CompId, f.Port, m => {
                    m.setMode("GUIDED");
                    m.doARM(true);
                    m.doCommand(MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, _baseAlt);
                });
            }
            _statusLbl.Text = "STATUS: SWARM TAKEOFF INITIATED";
        }

        private void ExecuteOnVehicle(byte sysid, byte compid, MAVLinkInterface port, Action<MAVLinkInterface> action)
        {
            lock (port)
            {
                var oldSys = port.sysidcurrent;
                var oldComp = port.compidcurrent;
                port.sysidcurrent = sysid;
                port.compidcurrent = compid;
                try { action(port); } catch { }
                port.sysidcurrent = oldSys;
                port.compidcurrent = oldComp;
            }
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

        public void OnLoop() { }
    }
}
