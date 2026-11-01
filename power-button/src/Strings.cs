using System.Globalization;

namespace Atlas8PowerButton
{
    /// <summary>Everything the user reads, in German when Windows is German and English otherwise.</summary>
    internal static class Strings
    {
        private static readonly bool German = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

        private static string T(string english, string german)
        {
            return German ? german : english;
        }

        public static string AppName => T("Atlas 8 Power Button", "Atlas 8 Power-Taste");

        public static string Action(PowerAction action)
        {
            switch (action)
            {
                case PowerAction.Sleep: return T("Sleep", "Energie sparen (Standby)");
                case PowerAction.Hibernate: return T("Hibernate", "Ruhezustand");
                case PowerAction.Shutdown: return T("Shut down", "Herunterfahren");
                case PowerAction.Restart: return T("Restart", "Neu starten");
                default: return action.ToString();
            }
        }

        public static string HibernateOff => T(" (switched off in Windows)", " (in Windows ausgeschaltet)");

        public static string Seconds(int ms)
        {
            var value = (ms / 1000.0).ToString("0.##", CultureInfo.CurrentCulture);
            return ms == 1000 ? T("1 second", "1 Sekunde") : T($"{value} seconds", $"{value} Sekunden");
        }

        // Settings window
        public static string SettingsIntro => T(
            "Choose what happens when you press the power button on your Atlas 8.",
            "Wähle aus, was passiert, wenn du die Power-Taste deines Atlas 8 drückst.");

        public static string ShortPress => T("Press briefly", "Kurz drücken");
        public static string LongPress => T("Press and hold", "Gedrückt halten");
        public static string HoldTime => T("Holding means at least", "Gedrückt halten heißt mindestens");

        public static string HoldHint => T(
            "Holding the button for many seconds always switches the Atlas 8 off by force. That is built into the device.",
            "Hältst du die Taste viele Sekunden lang, schaltet sich der Atlas 8 immer hart aus. Das ist im Gerät so eingebaut.");

        public static string UseSettings => T("Use these settings for the power button", "Diese Einstellungen für die Power-Taste verwenden");
        public static string ShowTrayIcon => T("Show an icon next to the clock", "Symbol neben der Uhr anzeigen");
        public static string Close => T("Close", "Schließen");
        public static string ShowLog => T("Show log for support", "Protokoll für den Support anzeigen");

        public static string StatusActive => T("Ready. Your power button works as chosen below.", "Bereit. Deine Power-Taste funktioniert wie unten gewählt.");
        public static string StatusPaused => T("Switched off. The power button works like before.", "Ausgeschaltet. Die Power-Taste funktioniert wie vorher.");
        public static string StatusNotFound => T("The power button could not be found. Please restart your Atlas 8.", "Die Power-Taste wurde nicht gefunden. Bitte starte deinen Atlas 8 neu.");
        public static string StatusNotRunning => T("Not running. Please restart your Atlas 8. If that does not help, run the setup again.", "Läuft nicht. Bitte starte deinen Atlas 8 neu. Hilft das nicht, führe das Setup erneut aus.");
        public static string StatusStarting => T("Starting...", "Wird gestartet...");
        public static string SaveFailed => T("Your choice could not be saved. Please run the setup again.", "Deine Auswahl konnte nicht gespeichert werden. Bitte führe das Setup erneut aus.");

        // Tray
        public static string TrayReady => T("Power button: ready", "Power-Taste: bereit");
        public static string TrayPaused => T("Power button: switched off", "Power-Taste: ausgeschaltet");
        public static string TrayProblem => T("Power button: needs attention", "Power-Taste: bitte prüfen");
        public static string TraySettings => T("Power button settings", "Einstellungen der Power-Taste");
        public static string TrayTurnOff => T("Switch off (power button works like before)", "Ausschalten (Power-Taste wie vorher)");
        public static string TrayTurnOn => T("Switch on", "Einschalten");
        public static string TrayHide => T("Hide this icon", "Dieses Symbol ausblenden");

        // Setup
        public static string SetupTitle => T("Atlas 8 Power Button Setup", "Atlas 8 Power-Taste einrichten");

        public static string SetupIntro => T(
            "On the Atlas 8, Windows only reacts when you hold the power button down. A short press does nothing.\n\nThis fixes that. After installing, a short press puts your Atlas 8 to sleep, and you can choose what pressing and holding do.",
            "Beim Atlas 8 reagiert Windows nur, wenn du die Power-Taste gedrückt hältst. Ein kurzer Druck macht nichts.\n\nDas behebt dieses Programm. Nach der Installation schickt ein kurzer Druck deinen Atlas 8 in den Standby, und du kannst wählen, was kurzes Drücken und Gedrückthalten tun.");

        public static string SetupInstall => T("Install", "Installieren");
        public static string SetupUpdate => T("Update", "Aktualisieren");
        public static string SetupReinstall => T("Repair", "Reparieren");
        public static string SetupRemove => T("Remove", "Entfernen");
        public static string SetupCancel => T("Cancel", "Abbrechen");
        public static string SetupWorking => T("Please wait...", "Bitte warten...");
        public static string SetupInstalled(string version) => T($"Version {version} is installed.", $"Version {version} ist installiert.");
        public static string SetupNotInstalled => T("Not installed yet.", "Noch nicht installiert.");
        public static string SetupDeviceOk => T("Acer Predator Atlas 8 found.", "Acer Predator Atlas 8 gefunden.");

        public static string SetupDone => T(
            "All done!\n\nTry it: press the power button briefly and your Atlas 8 goes to sleep. Press it again to wake it up.\n\nYou can change what the button does at any time: open \"Atlas 8 Power Button\" from the Start menu.",
            "Fertig!\n\nProbier es aus: Drück kurz die Power-Taste, und dein Atlas 8 geht in den Standby. Drück sie noch einmal, um ihn aufzuwecken.\n\nWas die Taste macht, kannst du jederzeit ändern: Öffne \"Atlas 8 Power-Taste\" im Startmenü.");

        public static string SetupRemoveConfirm => T(
            "Remove Atlas 8 Power Button?\n\nThe power button will work like before (only holding it does something).",
            "Atlas 8 Power-Taste entfernen?\n\nDie Power-Taste funktioniert dann wieder wie vorher (nur Gedrückthalten tut etwas).");

        public static string SetupRemoved => T(
            "Atlas 8 Power Button was removed. The power button works like before.",
            "Atlas 8 Power-Taste wurde entfernt. Die Power-Taste funktioniert wieder wie vorher.");

        public static string SetupFailed(string reason) => T(
            "Something went wrong:\n\n" + reason + "\n\nNothing was broken. Please restart your Atlas 8 and try again.",
            "Etwas ist schiefgelaufen:\n\n" + reason + "\n\nEs wurde nichts beschädigt. Bitte starte deinen Atlas 8 neu und versuche es erneut.");

        public static string SetupNeedsPermission => T(
            "The setup needs your permission to install. Please start it again and click \"Yes\" when Windows asks.",
            "Das Setup braucht deine Erlaubnis zum Installieren. Bitte starte es erneut und klicke auf \"Ja\", wenn Windows fragt.");

        public static string WrongDevice(string device) => T(
            $"This program only works on the Acer Predator Atlas 8.\n\nThis device is: {device}",
            $"Dieses Programm funktioniert nur auf dem Acer Predator Atlas 8.\n\nDieses Gerät ist: {device}");

        public static string NoButtonDevice => T(
            "The power button of your Atlas 8 could not be found. Please install the latest drivers from Acer and try again.",
            "Die Power-Taste deines Atlas 8 wurde nicht gefunden. Bitte installiere die neuesten Treiber von Acer und versuche es erneut.");
    }
}
