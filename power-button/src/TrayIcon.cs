using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>Optional icon next to the clock. The service does the work; this only shows and changes settings.</summary>
    internal sealed class TrayIcon : ApplicationContext
    {
        private readonly NotifyIcon _icon;
        private readonly ToolStripMenuItem _toggle;
        private readonly System.Windows.Forms.Timer _refresh = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly RegisteredWaitHandle _quitWait;
        private string _shown;

        private TrayIcon(EventWaitHandle quit)
        {
            var ui = SynchronizationContext.Current;
            _toggle = new ToolStripMenuItem("", null, (s, e) => Toggle());
            var menu = new ContextMenuStrip();
            var settings = menu.Items.Add(Strings.TraySettings, null, (s, e) => OpenSettings());
            settings.Font = new System.Drawing.Font(settings.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(_toggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.TrayHide, null, (s, e) =>
            {
                Installer.TrayAtSignIn = false;
                Exit();
            });
            _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    OpenSettings();
                }
            };
            _quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (s, t) => ui.Post(_ => Exit(), null), null, -1, true);
            _refresh.Tick += (s, e) => Refresh();
            _refresh.Start();
            Refresh();
        }

        public static int Run()
        {
            using (var mutex = new Mutex(true, Installer.TrayMutexName, out var created))
            {
                if (!created)
                {
                    return 0;
                }

                using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.TrayQuitEventName))
                {
                    // A Control must exist before the context is captured for the quit callback.
                    using (new Control())
                    {
                        Application.Run(new TrayIcon(quit));
                    }
                }

                mutex.ReleaseMutex();
            }

            return 0;
        }

        private void Refresh()
        {
            var settings = Settings.Load();
            var running = Installer.ServiceStatus == ServiceControllerStatus.Running;
            var status = running ? ServiceState.Load().Status : ServiceState.Stopped;
            var key = status + settings.Enabled;
            _toggle.Text = settings.Enabled ? Strings.TrayTurnOff : Strings.TrayTurnOn;
            if (key == _shown)
            {
                return;
            }

            _shown = key;
            var old = _icon.Icon;
            switch (status)
            {
                case ServiceState.Active:
                    _icon.Icon = AppIcon.Tray(SystemInformation.SmallIconSize.Width, AppIcon.Accent);
                    _icon.Text = Strings.TrayReady;
                    break;
                case ServiceState.Paused:
                    _icon.Icon = AppIcon.Tray(SystemInformation.SmallIconSize.Width, null);
                    _icon.Text = Strings.TrayPaused;
                    break;
                default:
                    _icon.Icon = AppIcon.Tray(SystemInformation.SmallIconSize.Width, Ui.Orange);
                    _icon.Text = Strings.TrayProblem;
                    break;
            }

            old?.Dispose();
        }

        private void Toggle()
        {
            var settings = Settings.Load();
            settings.Enabled = !settings.Enabled;
            settings.Save();
            _shown = null;
            Refresh();
        }

        private static void OpenSettings()
        {
            Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = false })?.Dispose();
        }

        private void Exit()
        {
            _refresh.Stop();
            _quitWait.Unregister(null);
            _icon.Visible = false;
            _icon.Dispose();
            ExitThread();
        }
    }
}
