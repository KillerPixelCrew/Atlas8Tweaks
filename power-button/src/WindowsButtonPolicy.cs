using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Atlas8PowerButton
{
    /// <summary>
    ///     Switches Windows' own power button action to "Do nothing" in every power plan and puts the
    ///     saved actions back. The saved values live in the settings file, so a crash or an uninstall
    ///     can still restore them.
    /// </summary>
    internal static class WindowsButtonPolicy
    {
        private const uint AccessScheme = 16;

        public static void TakeOver(ServiceState settings)
        {
            SlideToShutDownBlock.Enable();
            var changed = new List<string>();
            foreach (var scheme in Schemes())
            {
                var id = scheme;
                if (!Read(id, out var ac, out var dc))
                {
                    continue;
                }

                if (ac == 0 && dc == 0)
                {
                    continue;
                }

                settings.Originals[id] = new[] { ac, dc };
                if (Write(id, 0, 0))
                {
                    changed.Add($"{id:D} (was {ac}/{dc})");
                }
            }

            settings.Save();
            ApplyActive();
            Log.Write(changed.Count == 0
                ? "Windows power button handling already off"
                : "Windows power button handling off for " + string.Join(", ", changed));
        }

        public static void Restore(ServiceState settings)
        {
            SlideToShutDownBlock.Disable();
            if (settings.Originals.Count == 0)
            {
                return;
            }

            var restored = new List<string>();
            foreach (var original in new Dictionary<Guid, uint[]>(settings.Originals))
            {
                if (Write(original.Key, original.Value[0], original.Value[1]) || !SchemeExists(original.Key))
                {
                    settings.Originals.Remove(original.Key);
                    restored.Add($"{original.Key:D} = {original.Value[0]}/{original.Value[1]}");
                }
            }

            settings.Save();
            ApplyActive();
            Log.Write("Windows power button handling restored: " + string.Join(", ", restored));
        }

        private static bool Read(Guid scheme, out uint ac, out uint dc)
        {
            var sub = Native.GuidSubButtons;
            var setting = Native.GuidPowerButtonAction;
            dc = 0;
            return Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out ac) == 0
                   && Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out dc) == 0;
        }

        private static bool Write(Guid scheme, uint ac, uint dc)
        {
            var sub = Native.GuidSubButtons;
            var setting = Native.GuidPowerButtonAction;
            var a = Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, ac);
            var d = Native.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, dc);
            if (a != 0 || d != 0)
            {
                Log.Write($"Power plan {scheme:D}: write {ac}/{dc} failed ({a}, {d})");
            }

            return a == 0 && d == 0;
        }

        private static bool SchemeExists(Guid scheme)
        {
            foreach (var id in Schemes())
            {
                if (id == scheme)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Guid> Schemes()
        {
            var result = new List<Guid>();
            var buffer = new byte[16];
            for (uint i = 0;; i++)
            {
                uint size = 16;
                if (Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, i, buffer, ref size) != 0)
                {
                    break;
                }

                result.Add(new Guid(buffer));
            }

            return result;
        }

        /// <summary>Values written to the active plan take effect once the plan is set active again.</summary>
        private static void ApplyActive()
        {
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0)
            {
                return;
            }

            var active = (Guid)Marshal.PtrToStructure(pointer, typeof(Guid));
            Native.LocalFree(pointer);
            Native.PowerSetActiveScheme(IntPtr.Zero, ref active);
        }
    }
}
