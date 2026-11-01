using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>The window behind the Start menu entry: what a short press and a hold do.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly ComboBox _short;
        private readonly ComboBox _long;
        private readonly ComboBox _hold;
        private readonly CheckBox _enabled;
        private readonly CheckBox _tray;
        private readonly Ui.StatusLine _status;
        private readonly Timer _refresh = new Timer { Interval = 1000 };
        private Settings _settings;
        private bool _loading;
        private bool _saveFailed;

        public SettingsForm()
        {
            Ui.SetUpForm(this, Strings.AppName);
            var column = Ui.Column(this);
            column.Controls.Add(Ui.Header(this, Strings.AppName));
            column.Controls.Add(Ui.Text(this, Strings.SettingsIntro, below: 14));
            _status = new Ui.StatusLine(this);
            column.Controls.Add(_status);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Ui.S(this, 6))
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(this, 250)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _short = AddRow(grid, Strings.ShortPress);
            _long = AddRow(grid, Strings.LongPress);
            _hold = AddRow(grid, Strings.HoldTime);
            column.Controls.Add(grid);
            column.Controls.Add(Ui.Text(this, Strings.HoldHint, 9.5f, Ui.Muted, 16));

            _enabled = Ui.Check(this, Strings.UseSettings);
            _tray = Ui.Check(this, Strings.ShowTrayIcon);
            column.Controls.Add(_enabled);
            column.Controls.Add(_tray);

            var bottom = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, Ui.S(this, 16), 0, 0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var log = new LinkLabel
            {
                Text = Strings.ShowLog,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                LinkColor = Ui.Muted,
                Anchor = AnchorStyles.Left
            };
            log.LinkClicked += (s, e) => OpenLog();
            var close = Ui.Button(this, Strings.Close, true);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(log, 0, 0);
            bottom.Controls.Add(close, 1, 0);
            column.Controls.Add(bottom);
            Controls.Add(column);
            AcceptButton = close;
            CancelButton = close;

            LoadSettings();
            _short.SelectedIndexChanged += (s, e) => Save();
            _long.SelectedIndexChanged += (s, e) => Save();
            _hold.SelectedIndexChanged += (s, e) => Save();
            _enabled.CheckedChanged += (s, e) => Save();
            _tray.CheckedChanged += (s, e) => ToggleTray();
            _refresh.Tick += (s, e) => RefreshStatus();
            _refresh.Start();
            RefreshStatus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Dispose();
            }

            base.Dispose(disposing);
        }

        private ComboBox AddRow(TableLayoutPanel grid, string label)
        {
            var row = grid.RowCount++;
            grid.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                MaximumSize = new Size(Ui.S(this, 240), 0),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, Ui.S(this, 10), Ui.S(this, 12))
            }, 0, row);
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = Ui.S(this, 300),
                Font = new Font("Segoe UI", 12f),
                Margin = new Padding(0, 0, 0, Ui.S(this, 12)),
                Cursor = Cursors.Hand
            };
            grid.Controls.Add(combo, 1, row);
            return combo;
        }

        private void LoadSettings()
        {
            _loading = true;
            _settings = Settings.Load();
            Fill(_short, _settings.ShortPress);
            Fill(_long, _settings.LongPress);
            _hold.Items.Clear();
            foreach (var ms in Settings.LongPressChoices)
            {
                _hold.Items.Add(new Ui.Choice<int>(ms, Strings.Seconds(ms)));
                if (ms == _settings.LongPressMs)
                {
                    _hold.SelectedIndex = _hold.Items.Count - 1;
                }
            }

            if (_hold.SelectedIndex < 0)
            {
                _hold.SelectedIndex = Array.IndexOf(Settings.LongPressChoices, 1500);
            }

            _enabled.Checked = _settings.Enabled;
            _tray.Checked = Installer.TrayAtSignIn;
            _loading = false;
        }

        private static void Fill(ComboBox combo, PowerAction selected)
        {
            var hibernate = PowerActions.HibernateAvailable;
            combo.Items.Clear();
            foreach (var action in PowerActions.All)
            {
                var text = Strings.Action(action);
                if (action == PowerAction.Hibernate && !hibernate)
                {
                    text += Strings.HibernateOff;
                }

                combo.Items.Add(new Ui.Choice<PowerAction>(action, text));
                if (action == selected)
                {
                    combo.SelectedIndex = combo.Items.Count - 1;
                }
            }
        }

        private void Save()
        {
            if (_loading)
            {
                return;
            }

            _settings.ShortPress = ((Ui.Choice<PowerAction>)_short.SelectedItem).Value;
            _settings.LongPress = ((Ui.Choice<PowerAction>)_long.SelectedItem).Value;
            _settings.LongPressMs = ((Ui.Choice<int>)_hold.SelectedItem).Value;
            _settings.Enabled = _enabled.Checked;
            _saveFailed = !_settings.Save();
            _short.Enabled = _long.Enabled = _hold.Enabled = _settings.Enabled;
            RefreshStatus();
        }

        private void ToggleTray()
        {
            if (_loading)
            {
                return;
            }

            Installer.TrayAtSignIn = _tray.Checked;
            if (_tray.Checked)
            {
                Installer.StartTray();
            }
            else
            {
                Installer.StopTray();
            }
        }

        private void RefreshStatus()
        {
            _short.Enabled = _long.Enabled = _hold.Enabled = _enabled.Checked;
            if (_saveFailed)
            {
                _status.Show(Ui.Red, Strings.SaveFailed);
                return;
            }

            var service = Installer.ServiceStatus;
            if (service == ServiceControllerStatus.StartPending)
            {
                _status.Show(Ui.Grey, Strings.StatusStarting);
                return;
            }

            if (service != ServiceControllerStatus.Running)
            {
                _status.Show(Ui.Red, Strings.StatusNotRunning);
                return;
            }

            switch (ServiceState.Load().Status)
            {
                case ServiceState.Active:
                    _status.Show(Ui.Green, Strings.StatusActive);
                    break;
                case ServiceState.Paused:
                    _status.Show(Ui.Grey, Strings.StatusPaused);
                    break;
                case ServiceState.NotFound:
                    _status.Show(Ui.Orange, Strings.StatusNotFound);
                    break;
                default:
                    _status.Show(Ui.Grey, Strings.StatusStarting);
                    break;
            }
        }

        private static void OpenLog()
        {
            if (File.Exists(Log.FilePath))
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Log.FilePath}\"") { UseShellExecute = true })?.Dispose();
            }
        }
    }
}
