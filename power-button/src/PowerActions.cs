using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    internal enum PowerAction
    {
        Sleep,
        Hibernate,
        Shutdown,
        Restart
    }

    internal static class PowerActions
    {
        public static readonly PowerAction[] All =
        {
            PowerAction.Sleep, PowerAction.Hibernate, PowerAction.Shutdown, PowerAction.Restart
        };

        public static string Name(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.Sleep: return "Sleep (Modern Standby)";
                case PowerAction.Hibernate: return "Hibernation";
                case PowerAction.Shutdown: return "Shut down";
                case PowerAction.Restart: return "Reboot";
                default: return action.ToString();
            }
        }

        public static bool HibernateAvailable
        {
            get
            {
                // SYSTEM_POWER_CAPABILITIES: byte 6 is SystemS4, byte 8 HiberFilePresent.
                var caps = new byte[128];
                return Native.GetPwrCapabilities(caps) && caps[6] != 0 && caps[8] != 0;
            }
        }

        /// <summary>Runs an action from the service. Suspend calls return after resume, so they run off-thread.</summary>
        /// <param name="action">What to do.</param>
        /// <param name="resumed">Called on a worker thread after a suspend call returns.</param>
        public static void Execute(PowerAction action, Action resumed)
        {
            switch (action)
            {
                case PowerAction.Sleep:
                    // SetSuspendState and NtInitiatePowerAction both ask for S1-S3, which the Atlas does not
                    // have, so Windows hibernates instead (measured, event 187 then a firmware boot). Turning
                    // the display off is what enters Modern Standby here (event 506, "SC_MONITORPOWER").
                    SessionLauncher.Run(SessionLauncher.Standby);
                    return;
                case PowerAction.Hibernate:
                    Task.Run(() =>
                    {
                        if (!Native.SetSuspendState(true, false, false))
                        {
                            Log.Write($"Windows refused Hibernation (error {Marshal.GetLastWin32Error()})");
                        }

                        resumed();
                    });
                    return;
                case PowerAction.Shutdown:
                case PowerAction.Restart:
                    try
                    {
                        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                            action == PowerAction.Shutdown ? "/s /t 0" : "/r /t 0")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        })?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"{Name(action)} failed: {ex.Message}");
                    }

                    return;
            }
        }

        /// <summary>Runs a display action inside the console session; called in the helper process.</summary>
        public static int RunInSession(string verb)
        {
            // The STA main thread already owns a hidden COM window, and a thread with windows cannot
            // change its desktop. A fresh thread can.
            var result = 2;
            var thread = new System.Threading.Thread(() => result = RunOnInputDesktop(verb));
            thread.SetApartmentState(System.Threading.ApartmentState.MTA);
            thread.Start();
            thread.Join();
            return result;
        }

        private static int RunOnInputDesktop(string verb)
        {
            // The helper starts on the default desktop; the lock and sign-in screens are another one.
            var desktop = Native.OpenInputDesktop(0, false, 0x10000000);
            if (desktop != IntPtr.Zero && !Native.SetThreadDesktop(desktop))
            {
                Log.Write($"Session helper could not switch desktop (error {Marshal.GetLastWin32Error()})");
            }

            switch (verb)
            {
                case SessionLauncher.Wake:
                    const uint esDisplayRequired = 0x00000002;
                    Native.SetThreadExecutionState(esDisplayRequired);
                    var inputs = new[] { new Native.Input { Type = 0, Mouse = new Native.MouseInput { Flags = 0x0001 } } };
                    if (Native.SendInput(1, inputs, Marshal.SizeOf(typeof(Native.Input))) != 1)
                    {
                        Log.Write($"Display wake input failed (error {Marshal.GetLastWin32Error()})");
                        return 1;
                    }

                    return 0;
                case SessionLauncher.Standby:
                    var window = new NativeWindow();
                    window.CreateHandle(new CreateParams());
                    Native.SendMessage(window.Handle, Native.WmSysCommand, (IntPtr)Native.ScMonitorPower, (IntPtr)2);
                    window.DestroyHandle();
                    return 0;
                default:
                    return 2;
            }
        }
    }
}
