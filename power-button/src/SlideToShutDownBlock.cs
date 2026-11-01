using System;
using System.IO;
using Microsoft.Win32;

namespace Atlas8PowerButton
{
    /// <summary>
    ///     Holding the power button on a touch-screen PC makes winlogon start SlideToShutDown.exe, and no
    ///     power setting turns that off. While this tool handles the button, an Image File Execution
    ///     Options entry makes Windows start this exe with --slide-blocked instead, which exits at once.
    ///     Only our own value is ever written or removed; a foreign entry is left alone.
    /// </summary>
    internal static class SlideToShutDownBlock
    {
        public const string Flag = "slide-blocked";
        private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\SlideToShutDown.exe";

        private static string Command => $"\"{Path.Combine(Installer.InstallDirectory, "Atlas8PowerButton.exe")}\" --{Flag}";

        public static void Enable()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(KeyPath))
                {
                    var current = key.GetValue("Debugger") as string;
                    if (current == Command)
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(current))
                    {
                        Log.Write("SlideToShutDown already has another IFEO debugger; leaving it alone: " + current);
                        return;
                    }

                    key.SetValue("Debugger", Command);
                    Log.Write("Slide to shut down blocked");
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                Log.Write("Slide to shut down could not be blocked: " + ex.Message);
            }
        }

        public static void Disable()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(KeyPath, true))
                {
                    if (key == null || key.GetValue("Debugger") as string != Command)
                    {
                        return;
                    }

                    key.DeleteValue("Debugger", false);
                    var empty = key.ValueCount == 0 && key.SubKeyCount == 0;
                    key.Close();
                    if (empty)
                    {
                        Registry.LocalMachine.DeleteSubKey(KeyPath, false);
                    }

                    Log.Write("Slide to shut down allowed again");
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                Log.Write("Slide to shut down block could not be removed: " + ex.Message);
            }
        }
    }
}
