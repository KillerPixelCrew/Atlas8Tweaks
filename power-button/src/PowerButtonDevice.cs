using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Atlas8PowerButton
{
    /// <summary>
    ///     Reads the power button from the Intel HID Event Filter's Portable Device Control collection
    ///     (Generic Desktop 0x0D). It reports press and release of System Power Down (0x81) as they
    ///     happen, while Windows' own path only learns of the press after release.
    /// </summary>
    internal sealed class PowerButtonDevice : IDisposable
    {
        private const ushort GenericDesktop = 0x01;
        private const ushort PortableDeviceControl = 0x0D;
        private const ushort SystemPowerDown = 0x81;
        private const int HidpInput = 0;

        private readonly object _gate = new object();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _thread;
        private SafeFileHandle _handle;
        private bool _connected;

        /// <summary>Raised on the reader thread for every button report: true pressed, false released.</summary>
        public event Action<bool> Report;

        /// <summary>Raised on the reader thread when the device is opened or lost.</summary>
        public event Action<bool> ConnectedChanged;

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Power button reader" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop.Set();
            lock (_gate)
            {
                if (_handle != null && !_handle.IsInvalid)
                {
                    Native.CancelIoEx(_handle, IntPtr.Zero);
                }
            }

            _thread?.Join(2000);
        }

        /// <summary>Finds the collection without reading it. Used by setup for the device check.</summary>
        public static bool IsPresent()
        {
            var found = Find();
            if (found == null)
            {
                return false;
            }

            Native.HidD_FreePreparsedData(found.Preparsed);
            return true;
        }

        private void Run()
        {
            var reportedMissing = false;
            while (!_stop.WaitOne(0))
            {
                var found = Find();
                if (found == null)
                {
                    if (!reportedMissing)
                    {
                        Log.Write("Power button collection not found; retrying");
                        reportedMissing = true;
                    }

                    SetConnected(false);
                    _stop.WaitOne(3000);
                    continue;
                }

                reportedMissing = false;
                var handle = Native.CreateFile(found.Path, Native.GenericRead | Native.GenericWrite,
                    Native.FileShareReadWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    Log.Write($"Power button collection could not be opened (error {Marshal.GetLastWin32Error()})");
                    handle.Dispose();
                    Native.HidD_FreePreparsedData(found.Preparsed);
                    SetConnected(false);
                    _stop.WaitOne(3000);
                    continue;
                }

                lock (_gate)
                {
                    _handle = handle;
                }

                Log.Write($"Reading power button (report length {found.InputLength})");
                SetConnected(true);
                ReadLoop(handle, found);
                lock (_gate)
                {
                    _handle = null;
                }

                handle.Dispose();
                Native.HidD_FreePreparsedData(found.Preparsed);
                if (!_stop.WaitOne(0))
                {
                    SetConnected(false);
                    _stop.WaitOne(1000);
                }
            }
        }

        private void ReadLoop(SafeFileHandle handle, Found found)
        {
            var report = new byte[found.InputLength];
            var usages = new ushort[8];
            while (!_stop.WaitOne(0))
            {
                if (!Native.ReadFile(handle, report, report.Length, out var read, IntPtr.Zero) || read == 0)
                {
                    if (!_stop.WaitOne(0))
                    {
                        Log.Write($"Power button read stopped (error {Marshal.GetLastWin32Error()})");
                    }

                    return;
                }

                uint count = (uint)usages.Length;
                var status = Native.HidP_GetUsages(HidpInput, GenericDesktop, 0, usages, ref count, found.Preparsed,
                    report, (uint)read);
                if (status != Native.HidpStatusSuccess)
                {
                    continue;
                }

                var pressed = false;
                for (var i = 0; i < count; i++)
                {
                    pressed |= usages[i] == SystemPowerDown;
                }

                Report?.Invoke(pressed);
            }
        }

        private void SetConnected(bool connected)
        {
            if (_connected == connected)
            {
                return;
            }

            _connected = connected;
            ConnectedChanged?.Invoke(connected);
        }

        private static Found Find()
        {
            var hid = Native.GuidDevInterfaceHid;
            var set = Native.SetupDiGetClassDevs(ref hid, null, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                var data = new Native.SpDeviceInterfaceData { Size = Marshal.SizeOf(typeof(Native.SpDeviceInterfaceData)) };
                for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref data); i++)
                {
                    var path = InterfacePath(set, ref data);
                    var found = path == null ? null : Inspect(path);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }

            return null;
        }

        private static string InterfacePath(IntPtr set, ref Native.SpDeviceInterfaceData data)
        {
            Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
            if (size == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.WriteInt32(buffer, 8);
                return Native.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero)
                    ? Marshal.PtrToStringUni(buffer + 4)
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static Found Inspect(string path)
        {
            using (var probe = Native.CreateFile(path, 0, Native.FileShareReadWrite, IntPtr.Zero, Native.OpenExisting, 0,
                       IntPtr.Zero))
            {
                if (probe.IsInvalid || !Native.HidD_GetPreparsedData(probe, out var preparsed))
                {
                    return null;
                }

                var caps = new byte[64];
                if (Native.HidP_GetCaps(preparsed, caps) == Native.HidpStatusSuccess
                    && BitConverter.ToUInt16(caps, 2) == GenericDesktop
                    && BitConverter.ToUInt16(caps, 0) == PortableDeviceControl
                    && HasPowerButton(preparsed))
                {
                    return new Found { Path = path, Preparsed = preparsed, InputLength = BitConverter.ToUInt16(caps, 4) };
                }

                Native.HidD_FreePreparsedData(preparsed);
                return null;
            }
        }

        private static bool HasPowerButton(IntPtr preparsed)
        {
            // HIDP_BUTTON_CAPS is 72 bytes: UsagePage at 0, IsRange at 12, Usage or UsageMin at 56, UsageMax at 58.
            const int size = 72;
            ushort count = 16;
            var caps = new byte[size * count];
            if (Native.HidP_GetButtonCaps(HidpInput, caps, ref count, preparsed) != Native.HidpStatusSuccess)
            {
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                var offset = i * size;
                if (BitConverter.ToUInt16(caps, offset) != GenericDesktop)
                {
                    continue;
                }

                var min = BitConverter.ToUInt16(caps, offset + 56);
                var max = caps[offset + 12] != 0 ? BitConverter.ToUInt16(caps, offset + 58) : min;
                if (min <= SystemPowerDown && SystemPowerDown <= max)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Found
        {
            public string Path;
            public IntPtr Preparsed;
            public int InputLength;
        }
    }
}
