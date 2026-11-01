using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>
    ///     The service's work: reads the button, times presses, runs actions and keeps Windows' own
    ///     handling off while it can read the button. Everything runs on one message-loop thread; the
    ///     reader posts its reports to it.
    /// </summary>
    internal sealed class ButtonEngine
    {
        // A press right after the display comes on or the system resumes belongs to the wake itself.
        private const int WakeGuardMs = 1500;

        // A second report within this window after an action is a bounce, not a new press.
        private const int ActionGuardMs = 400;

        private readonly SynchronizationContext _ui;
        private readonly ServiceState _state;
        private readonly PowerButtonDevice _device = new PowerButtonDevice();
        private readonly MessageWindow _window;
        private readonly System.Windows.Forms.Timer _lostTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        private readonly System.Windows.Forms.Timer _reloadTimer = new System.Windows.Forms.Timer { Interval = 300 };
        private readonly FileSystemWatcher _watcher;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private Settings _settings;
        private bool _connected;
        private bool _takenOver;
        private bool _displayOff;
        private bool _down;
        private bool _consumed;
        private long _downAt;
        private long _guardUntil;
        private bool _stopped;

        /// <summary>Must be constructed on the thread that will run the message loop.</summary>
        public ButtonEngine()
        {
            _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("No message loop context");
            _settings = Settings.Load();
            _state = ServiceState.Load();
            _window = new MessageWindow(this);
            _lostTimer.Tick += (s, e) =>
            {
                _lostTimer.Stop();
                UpdateTakeover();
            };
            _reloadTimer.Tick += (s, e) =>
            {
                _reloadTimer.Stop();
                ReloadSettings();
            };

            Directory.CreateDirectory(Settings.DataDirectory);
            _watcher = new FileSystemWatcher(Settings.DataDirectory, "settings.ini")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            FileSystemEventHandler changed = (s, e) => _ui.Post(_ =>
            {
                _reloadTimer.Stop();
                _reloadTimer.Start();
            }, null);
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Renamed += (s, e) => changed(s, e);
            _watcher.EnableRaisingEvents = true;

            _device.Report += pressed => _ui.Post(_ => OnReport(pressed), null);
            _device.ConnectedChanged += connected => _ui.Post(_ => OnConnectedChanged(connected), null);

            SessionLauncher.EnableShutdownPrivilege();
            Log.Write($"Service {Installer.Version} started: {Describe(_settings)}");

            // Values left from a crash are restored until the button can actually be read again.
            WindowsButtonPolicy.Restore(_state);
            PublishStatus();
            _device.Start();
        }

        private long Now => _clock.ElapsedMilliseconds;

        public void Post(Action action)
        {
            _ui.Post(_ => action(), null);
        }

        /// <summary>Restores Windows' handling and releases everything. Runs on the loop thread.</summary>
        public void Stop(string reason)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            Log.Write("Service stopping: " + reason);
            _watcher.Dispose();
            _device.Dispose();
            if (_takenOver)
            {
                WindowsButtonPolicy.Restore(_state);
                _takenOver = false;
            }

            _state.Status = ServiceState.Stopped;
            _state.Save();
            _window.Dispose();
            Application.ExitThread();
        }

        public void OnSuspend()
        {
            _down = false;
            Log.Write("Suspending");
        }

        public void OnResumed(string reason)
        {
            _down = false;
            _guardUntil = Now + WakeGuardMs;
            Log.Write("Resume: " + reason);
        }

        private static string Describe(Settings settings)
        {
            return $"short={settings.ShortPress}, long={settings.LongPress} after {settings.LongPressMs} ms, enabled={settings.Enabled}";
        }

        private void ReloadSettings()
        {
            if (_stopped)
            {
                return;
            }

            _settings = Settings.Load();
            Log.Write("Settings: " + Describe(_settings));
            UpdateTakeover();
        }

        private void OnConnectedChanged(bool connected)
        {
            _connected = connected;
            if (connected)
            {
                _lostTimer.Stop();
                UpdateTakeover();
                return;
            }

            // A short loss (for example around standby) keeps the takeover; a lasting one gives the button back.
            _down = false;
            _lostTimer.Stop();
            _lostTimer.Start();
        }

        private void UpdateTakeover()
        {
            if (_stopped)
            {
                return;
            }

            var want = _settings.Enabled && _connected;
            if (want && !_takenOver)
            {
                WindowsButtonPolicy.TakeOver(_state);
                _takenOver = true;
            }
            else if (!want && _takenOver)
            {
                WindowsButtonPolicy.Restore(_state);
                _takenOver = false;
            }

            PublishStatus();
        }

        private void PublishStatus()
        {
            var status = !_settings.Enabled ? ServiceState.Paused : _takenOver ? ServiceState.Active : ServiceState.NotFound;
            if (_state.Status != status)
            {
                _state.Status = status;
                _state.Save();
            }
        }

        private void OnReport(bool pressed)
        {
            if (!_takenOver || _stopped)
            {
                return;
            }

            if (pressed)
            {
                if (_down)
                {
                    return;
                }

                _down = true;
                _downAt = Now;
                _consumed = false;
                if (_displayOff)
                {
                    _consumed = true;
                    Log.Write("Press while the display is off: waking the display");
                    SessionLauncher.Run(SessionLauncher.Wake);
                    return;
                }

                if (Now < _guardUntil)
                {
                    _consumed = true;
                    Log.Write("Press ignored: part of a wake or a bounce");
                    return;
                }

                return;
            }

            var wasDown = _down;
            _down = false;
            if (wasDown && _consumed)
            {
                return;
            }

            if (_displayOff)
            {
                Log.Write("Release while the display is off: waking the display");
                SessionLauncher.Run(SessionLauncher.Wake);
                return;
            }

            if (Now < _guardUntil)
            {
                Log.Write("Release ignored: part of a wake or a bounce");
                return;
            }

            // The firmware sometimes reports only the release of a quick tap.
            if (!wasDown)
            {
                Log.Write("Short press (release only)");
                Execute(_settings.ShortPress);
                return;
            }

            // Both actions run on release. Suspending while the button is still held leaves Windows
            // thinking it was held through the sleep, and it then offers its own slide-to-shut-down.
            var held = Now - _downAt;
            var isLong = held >= _settings.LongPressMs;
            Log.Write($"{(isLong ? "Long" : "Short")} press ({held} ms)");
            Execute(isLong ? _settings.LongPress : _settings.ShortPress);
        }

        private void Execute(PowerAction action)
        {
            _guardUntil = Now + ActionGuardMs;
            Log.Write("Action: " + PowerActions.Name(action));
            PowerActions.Execute(action, () => Post(() => OnResumed("suspend call returned")));
        }

        private void OnDisplayState(uint state)
        {
            // 0 off, 1 on, 2 dimmed. A dimmed display still takes the normal press action.
            var off = state == 0;
            if (off != _displayOff)
            {
                Log.Write(off ? "Display off" : "Display on");
            }

            if (_displayOff && !off)
            {
                _guardUntil = Math.Max(_guardUntil, Now + WakeGuardMs);
            }

            _displayOff = off;
        }

        private void OnPowerButtonActionChanged(uint value)
        {
            if (_takenOver && value != 0)
            {
                Log.Write($"Windows power button action changed to {value} elsewhere; taking it over again");
                WindowsButtonPolicy.TakeOver(_state);
            }
        }

        private void OnPlanSwitched(Guid plan)
        {
            if (_takenOver)
            {
                Log.Write($"Power plan switched to {plan:D}");
                WindowsButtonPolicy.TakeOver(_state);
            }
        }

        /// <summary>Hidden window for power setting notifications.</summary>
        private sealed class MessageWindow : NativeWindow, IDisposable
        {
            private readonly ButtonEngine _owner;
            private readonly IntPtr[] _registrations = new IntPtr[3];
            private Guid _activePlan;

            public MessageWindow(ButtonEngine owner)
            {
                _owner = owner;
                CreateHandle(new CreateParams { Caption = "Atlas8PowerButton" });
                Register(0, Native.GuidConsoleDisplayState);
                Register(1, Native.GuidPowerButtonAction);
                Register(2, Native.GuidActivePowerScheme);
            }

            public void Dispose()
            {
                foreach (var registration in _registrations)
                {
                    if (registration != IntPtr.Zero)
                    {
                        Native.UnregisterPowerSettingNotification(registration);
                    }
                }

                DestroyHandle();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WmPowerBroadcast)
                {
                    if (m.WParam.ToInt32() == Native.PbtPowerSettingChange)
                    {
                        OnSetting(m.LParam);
                    }

                    m.Result = (IntPtr)1;
                    return;
                }

                base.WndProc(ref m);
            }

            private void OnSetting(IntPtr data)
            {
                var setting = (Native.PowerBroadcastSetting)Marshal.PtrToStructure(data, typeof(Native.PowerBroadcastSetting));
                if (setting.PowerSetting == Native.GuidConsoleDisplayState)
                {
                    _owner.OnDisplayState(setting.Data);
                }
                else if (setting.PowerSetting == Native.GuidPowerButtonAction)
                {
                    _owner.OnPowerButtonActionChanged(setting.Data);
                }
                else if (setting.PowerSetting == Native.GuidActivePowerScheme && setting.DataLength >= 16)
                {
                    // Only a real plan switch counts; TakeOver itself re-activates the same plan.
                    var plan = (Guid)Marshal.PtrToStructure(data + 20, typeof(Guid));
                    if (plan != _activePlan)
                    {
                        var first = _activePlan == Guid.Empty;
                        _activePlan = plan;
                        if (!first)
                        {
                            _owner.OnPlanSwitched(plan);
                        }
                    }
                }
            }

            private void Register(int slot, Guid setting)
            {
                _registrations[slot] = Native.RegisterPowerSettingNotification(Handle, ref setting, 0);
                if (_registrations[slot] == IntPtr.Zero)
                {
                    Log.Write($"Power notification {setting:D} unavailable (error {Marshal.GetLastWin32Error()})");
                }
            }
        }
    }
}
