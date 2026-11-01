using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace Atlas8PowerButton
{
    /// <summary>Machine-wide install: the exe in Program Files, the service, and an optional per-user tray icon.</summary>
    internal static class Installer
    {
        public const string TrayMutexName = @"Local\Atlas8PowerButton.Tray";
        public const string TrayQuitEventName = @"Local\Atlas8PowerButton.TrayQuit";
        public const string SettingsMutexName = @"Local\Atlas8PowerButton.Settings";
        public const string SettingsShowEventName = @"Local\Atlas8PowerButton.SettingsShow";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Atlas8PowerButton";
        private const string RunValue = "Atlas8PowerButton";

        public static string InstallDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Atlas8PowerButton");

        public static string InstalledExe => Path.Combine(InstallDirectory, "Atlas8PowerButton.exe");

        private static string Shortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Atlas 8 Power Button.lnk");

        public static string SelfPath => Assembly.GetExecutingAssembly().Location;

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public static bool RunningFromInstallDirectory =>
            string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        public static bool IsInstalled => File.Exists(InstalledExe);

        public static string InstalledVersion
        {
            get
            {
                if (!IsInstalled)
                {
                    return null;
                }

                var info = FileVersionInfo.GetVersionInfo(InstalledExe);
                return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
            }
        }

        public static bool IsElevated
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
        }

        /// <summary>Whether this user's tray icon starts at sign-in.</summary>
        public static bool TrayAtSignIn
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key?.GetValue(RunValue) != null;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value)
                    {
                        key.SetValue(RunValue, $"\"{InstalledExe}\" --tray");
                    }
                    else
                    {
                        key.DeleteValue(RunValue, false);
                    }
                }
            }
        }

        public static ServiceControllerStatus? ServiceStatus
        {
            get
            {
                try
                {
                    using (var service = new ServiceController(PowerButtonService.Name))
                    {
                        return service.Status;
                    }
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        /// <summary>Checks the model and that the power button collection exists, without reading it.</summary>
        public static string DeviceProblem()
        {
            string manufacturer;
            string product;
            using (var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
            {
                manufacturer = bios?.GetValue("SystemManufacturer") as string ?? "";
                product = bios?.GetValue("SystemProductName") as string ?? "";
            }

            if (manufacturer.IndexOf("Acer", StringComparison.OrdinalIgnoreCase) < 0
                || product.IndexOf("PA08", StringComparison.OrdinalIgnoreCase) < 0)
            {
                var name = (manufacturer + " " + product).Trim();
                return Strings.WrongDevice(name.Length == 0 ? "?" : name);
            }

            return PowerButtonDevice.IsPresent() ? null : Strings.NoButtonDevice;
        }

        /// <summary>Installs or updates. Requires administrator rights.</summary>
        public static void Install(bool trayIcon)
        {
            StopTray();
            StopService();
            Directory.CreateDirectory(InstallDirectory);
            if (!RunningFromInstallDirectory)
            {
                CopyWithRetry(SelfPath, InstalledExe);
            }

            PrepareDataDirectory();
            var bin = $"\"{InstalledExe}\" --service";
            if (ServiceStatus == null)
            {
                Sc($"create {PowerButtonService.Name} binPath= \"{bin.Replace("\"", "\\\"")}\" start= auto DisplayName= \"Atlas 8 Power Button\"");
            }
            else
            {
                Sc($"config {PowerButtonService.Name} binPath= \"{bin.Replace("\"", "\\\"")}\" start= auto");
            }

            Sc($"description {PowerButtonService.Name} \"Handles short and long presses of the Atlas 8 power button.\"");
            Sc($"failure {PowerButtonService.Name} reset= 86400 actions= restart/5000/restart/5000/restart/30000");
            using (var service = new ServiceController(PowerButtonService.Name))
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            }

            using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "Atlas 8 Power Button");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Killer Pixel Crew");
                key.SetValue("DisplayIcon", InstalledExe);
                key.SetValue("InstallLocation", InstallDirectory);
                key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
                key.SetValue("URLInfoAbout", "https://github.com/KillerPixelCrew/Atlas8Tweaks");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }

            CreateShortcut();
            TrayAtSignIn = trayIcon;
            Log.Write($"Installed {Version} (tray icon: {trayIcon})");
        }

        /// <summary>Removes everything and gives the button back to Windows. Requires administrator rights.</summary>
        public static void Uninstall()
        {
            StopTray();
            StopService();
            if (ServiceStatus != null)
            {
                Sc($"delete {PowerButtonService.Name}");
            }

            // The service restores Windows' handling when it stops; this covers a service that had crashed.
            WindowsButtonPolicy.Restore(ServiceState.Load());
            TrayAtSignIn = false;
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
            TryDelete(() => File.Delete(Shortcut));
            TryDelete(() => Directory.Delete(Settings.DataDirectory, true));

            if (RunningFromInstallDirectory)
            {
                // A running exe cannot delete itself; cmd removes the folder once this process has exited.
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDirectory}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                })?.Dispose();
            }
            else
            {
                TryDelete(() => Directory.Delete(InstallDirectory, true));
            }
        }

        /// <summary>Starts the installed exe as the signed-in user, not elevated, through Explorer.</summary>
        public static void LaunchAsUser()
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                $"\"{InstalledExe}\"") { UseShellExecute = false })?.Dispose();
        }

        public static void StartTray()
        {
            if (!IsRunning(TrayMutexName))
            {
                Process.Start(new ProcessStartInfo(InstalledExe, "--tray") { UseShellExecute = false })?.Dispose();
            }
        }

        public static void StopTray()
        {
            try
            {
                using (var quit = EventWaitHandle.OpenExisting(TrayQuitEventName))
                {
                    quit.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return;
            }

            for (var i = 0; i < 30 && IsRunning(TrayMutexName); i++)
            {
                Thread.Sleep(100);
            }
        }

        public static bool IsRunning(string mutexName)
        {
            try
            {
                Mutex.OpenExisting(mutexName).Dispose();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static void StopService()
        {
            try
            {
                using (var service = new ServiceController(PowerButtonService.Name))
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Not installed.
            }
        }

        /// <summary>ProgramData folder the service writes and every user's settings window can edit.</summary>
        private static void PrepareDataDirectory()
        {
            var directory = Directory.CreateDirectory(Settings.DataDirectory);
            var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
            if (!File.Exists(Settings.FilePath))
            {
                new Settings().Save();
            }
        }

        private static void Sc(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments)
                   {
                       UseShellExecute = false,
                       CreateNoWindow = true,
                       RedirectStandardOutput = true
                   }))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"sc {arguments.Split(' ')[0]} failed ({process.ExitCode}): {output.Trim()}");
                }
            }
        }

        private static void CopyWithRetry(string source, string target)
        {
            for (var attempt = 0;; attempt++)
            {
                try
                {
                    File.Copy(source, target, true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    // The stopped service or tray may hold the file for a moment.
                    Thread.Sleep(250);
                }
            }
        }

        private static void CreateShortcut()
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(type);
                var link = shell.CreateShortcut(Shortcut);
                link.TargetPath = InstalledExe;
                link.WorkingDirectory = InstallDirectory;
                link.Description = "Atlas 8 Power Button settings";
                link.IconLocation = InstalledExe + ",0";
                link.Save();
            }
            catch (Exception ex)
            {
                Log.Write("Start menu shortcut could not be created: " + ex.Message);
            }
        }

        private static void TryDelete(Action delete)
        {
            try
            {
                delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
