using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>
    ///     One exe for everything. Started from anywhere else it is the setup; from Program Files it is the
    ///     settings window; with arguments it is the service, the tray icon or a short display helper.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var flags = args.Select(a => a.TrimStart('-', '/').ToLowerInvariant()).ToArray();

            if (flags.Contains(SlideToShutDownBlock.Flag))
            {
                // Started by Windows in place of SlideToShutDown.exe while the button is held.
                return 0;
            }

            if (flags.Contains("service"))
            {
                ServiceBase.Run(new PowerButtonService());
                return 0;
            }

            if (flags.Contains("session") && flags.Length > 1)
            {
                return PowerActions.RunInSession(flags[Array.IndexOf(flags, "session") + 1]);
            }

            if (flags.Contains("foreground"))
            {
                // For testing on the device without installing the service.
                var index = Array.IndexOf(flags, "foreground") + 1;
                var seconds = index < flags.Length && int.TryParse(flags[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 120;
                PowerButtonService.RunForeground(seconds);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (flags.Contains("render") && flags.Length >= 4)
            {
                // Developer check: --render setup|settings <en|de> <png>.
                var i = Array.IndexOf(flags, "render");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(flags[i + 2]);
                return Render(flags[i + 1], args[i + 3]);
            }

            if (flags.Contains("uninstall"))
            {
                return Uninstall(args, flags.Contains("quiet"));
            }

            if (flags.Contains("tray"))
            {
                return TrayIcon.Run();
            }

            if (Installer.RunningFromInstallDirectory)
            {
                return ShowSettings();
            }

            if (!Installer.IsElevated)
            {
                return RelaunchElevated(args);
            }

            Application.Run(new SetupForm());
            return 0;
        }

        private static int ShowSettings()
        {
            using (var mutex = new Mutex(true, Installer.SettingsMutexName, out var created))
            {
                if (!created)
                {
                    try
                    {
                        using (var show = EventWaitHandle.OpenExisting(Installer.SettingsShowEventName))
                        {
                            show.Set();
                        }
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                    }

                    return 0;
                }

                if (Installer.TrayAtSignIn)
                {
                    Installer.StartTray();
                }

                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.SettingsShowEventName))
                using (var form = new SettingsForm())
                {
                    var ui = SynchronizationContext.Current;
                    var wait = ThreadPool.RegisterWaitForSingleObject(show, (s, t) => ui.Post(_ =>
                    {
                        form.WindowState = FormWindowState.Normal;
                        form.Activate();
                    }, null), null, -1, false);
                    Application.Run(form);
                    wait.Unregister(null);
                }

                mutex.ReleaseMutex();
            }

            return 0;
        }

        private static int Uninstall(string[] args, bool quiet)
        {
            if (!Installer.IsElevated)
            {
                return RelaunchElevated(args);
            }

            if (!quiet && !SetupForm.Confirm(Strings.SetupRemoveConfirm))
            {
                return 1;
            }

            try
            {
                Installer.Uninstall();
            }
            catch (Exception ex)
            {
                Log.Write("Uninstall failed: " + ex);
                if (!quiet)
                {
                    MessageBox.Show(Strings.SetupFailed(ex.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return 1;
            }

            if (!quiet)
            {
                MessageBox.Show(Strings.SetupRemoved, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return 0;
        }

        private static int Render(string which, string path)
        {
            using (Form form = which == "setup" ? (Form)new SetupForm() : new SettingsForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            return 0;
        }

        private static int RelaunchElevated(string[] args)
        {
            try
            {
                var arguments = string.Join(" ", args.Select(a => "\"" + a + "\""));
                Process.Start(new ProcessStartInfo(Installer.SelfPath, arguments) { UseShellExecute = true, Verb = "runas" })?.Dispose();
                return 0;
            }
            catch (Win32Exception)
            {
                MessageBox.Show(Strings.SetupNeedsPermission, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
    }
}
