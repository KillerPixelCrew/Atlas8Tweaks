using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Atlas8PowerButton
{
    /// <summary>
    ///     The service runs in session 0, which has no display or desktop. Display actions start this exe
    ///     briefly in the console session as SYSTEM, so they also work on the lock and sign-in screens.
    /// </summary>
    internal static class SessionLauncher
    {
        public const string Wake = "wake";
        public const string Standby = "standby";

        public static void Run(string verb)
        {
            var session = Native.WTSGetActiveConsoleSessionId();
            if (session == 0xFFFFFFFF)
            {
                Log.Write($"No console session for {verb}");
                return;
            }

            var token = IntPtr.Zero;
            try
            {
                if (!SystemTokenFor(session, out token))
                {
                    return;
                }

                var exe = Installer.SelfPath;
                var startup = new Native.StartupInfo
                {
                    Size = Marshal.SizeOf(typeof(Native.StartupInfo)),
                    Desktop = @"winsta0\default"
                };
                if (!Native.CreateProcessAsUser(token, exe, $"\"{exe}\" --session {verb}", IntPtr.Zero, IntPtr.Zero, false,
                        Native.CreateNoWindow, IntPtr.Zero, Path.GetDirectoryName(exe), ref startup, out var process))
                {
                    Log.Write($"{verb}: helper could not start (error {Marshal.GetLastWin32Error()})");
                    return;
                }

                Native.CloseHandle(process.Thread);
                Native.CloseHandle(process.Process);
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    Native.CloseHandle(token);
                }
            }
        }

        private static bool SystemTokenFor(uint session, out IntPtr token)
        {
            token = IntPtr.Zero;
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TokenAllAccess, out var self))
            {
                Log.Write($"OpenProcessToken failed (error {Marshal.GetLastWin32Error()})");
                return false;
            }

            try
            {
                if (!Native.DuplicateTokenEx(self, Native.TokenAllAccess, IntPtr.Zero, 2, 1, out token))
                {
                    Log.Write($"DuplicateTokenEx failed (error {Marshal.GetLastWin32Error()})");
                    token = IntPtr.Zero;
                    return false;
                }

                if (!Native.SetTokenInformation(token, 12, ref session, 4))
                {
                    Log.Write($"Setting the helper session failed (error {Marshal.GetLastWin32Error()})");
                    Native.CloseHandle(token);
                    token = IntPtr.Zero;
                    return false;
                }

                return true;
            }
            finally
            {
                Native.CloseHandle(self);
            }
        }

        /// <summary>SetSuspendState needs the shutdown privilege enabled in the calling token.</summary>
        public static void EnableShutdownPrivilege()
        {
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), 0x0020 | 0x0008, out var token))
            {
                return;
            }

            try
            {
                if (!Native.LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
                {
                    return;
                }

                var privileges = new Native.TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                if (!Native.AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    Log.Write($"Shutdown privilege could not be enabled (error {Marshal.GetLastWin32Error()})");
                }
            }
            finally
            {
                Native.CloseHandle(token);
            }
        }
    }
}
