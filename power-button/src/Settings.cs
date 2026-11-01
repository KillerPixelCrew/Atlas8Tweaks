using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Atlas8PowerButton
{
    /// <summary>The user's choices. Written by the settings window, read by the service.</summary>
    internal sealed class Settings
    {
        public static readonly int[] LongPressChoices = { 500, 750, 1000, 1500, 2000, 2500, 3000 };

        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Atlas8PowerButton");

        public static string FilePath => Path.Combine(DataDirectory, "settings.ini");

        public PowerAction ShortPress { get; set; } = PowerAction.Sleep;
        public PowerAction LongPress { get; set; } = PowerAction.Shutdown;
        public int LongPressMs { get; set; } = 1500;
        public bool Enabled { get; set; } = true;

        public static Settings Load()
        {
            var settings = new Settings();
            foreach (var pair in IniFile.Read(FilePath))
            {
                switch (pair.Key)
                {
                    case "ShortPress":
                        settings.ShortPress = ParseAction(pair.Value, settings.ShortPress);
                        break;
                    case "LongPress":
                        settings.LongPress = ParseAction(pair.Value, settings.LongPress);
                        break;
                    case "LongPressMs":
                        if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                        {
                            settings.LongPressMs = Math.Max(500, Math.Min(3000, ms));
                        }

                        break;
                    case "Enabled":
                        settings.Enabled = !string.Equals(pair.Value, "false", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return settings;
        }

        public bool Save()
        {
            var text = new StringBuilder();
            text.AppendLine("ShortPress=" + ShortPress);
            text.AppendLine("LongPress=" + LongPress);
            text.AppendLine("LongPressMs=" + LongPressMs.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Enabled=" + (Enabled ? "true" : "false"));
            return IniFile.Write(FilePath, text.ToString());
        }

        private static PowerAction ParseAction(string value, PowerAction fallback)
        {
            return Enum.TryParse(value, out PowerAction action) && Enum.IsDefined(typeof(PowerAction), action)
                ? action
                : fallback;
        }
    }

    /// <summary>Written by the service only: what it is doing and the Windows actions it replaced.</summary>
    internal sealed class ServiceState
    {
        public const string Active = "active";
        public const string Paused = "paused";
        public const string NotFound = "notfound";
        public const string Stopped = "stopped";

        public static string FilePath => Path.Combine(Settings.DataDirectory, "state.ini");

        public string Status { get; set; } = Stopped;

        /// <summary>Original AC/DC power button action per power plan, kept until it is restored.</summary>
        public Dictionary<Guid, uint[]> Originals { get; } = new Dictionary<Guid, uint[]>();

        public static ServiceState Load()
        {
            var state = new ServiceState();
            foreach (var pair in IniFile.Read(FilePath))
            {
                if (pair.Key == "Status")
                {
                    state.Status = pair.Value;
                }
                else
                {
                    ReadPair(pair, "Original.", state.Originals);
                }
            }

            return state;
        }

        public void Save()
        {
            var text = new StringBuilder();
            text.AppendLine("Status=" + Status);
            foreach (var original in Originals)
            {
                text.AppendLine($"Original.{original.Key:D}={original.Value[0]},{original.Value[1]}");
            }

            IniFile.Write(FilePath, text.ToString());
        }

        private static void ReadPair(KeyValuePair<string, string> pair, string prefix, Dictionary<Guid, uint[]> target)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(pair.Key.Substring(prefix.Length), out var plan))
            {
                return;
            }

            var parts = pair.Value.Split(',');
            if (parts.Length == 2
                && uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ac)
                && uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc))
            {
                target[plan] = new[] { ac, dc };
            }
        }
    }

    internal static class IniFile
    {
        public static IEnumerable<KeyValuePair<string, string>> Read(string path)
        {
            string[] lines;
            try
            {
                if (!File.Exists(path))
                {
                    yield break;
                }

                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                yield break;
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var raw in lines)
            {
                var split = raw.IndexOf('=');
                if (split > 0)
                {
                    yield return new KeyValuePair<string, string>(raw.Substring(0, split).Trim(), raw.Substring(split + 1).Trim());
                }
            }
        }

        /// <summary>Writes through a temporary file so a reader never sees half a file.</summary>
        public static bool Write(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Write($"Could not write {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }
    }
}
