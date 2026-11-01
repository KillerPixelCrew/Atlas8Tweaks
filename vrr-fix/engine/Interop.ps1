# Intel driver interop for the Acer Atlas 8 VRR fix.
#
# Both C# sources below are taken unchanged from the ClawLab MSI Claw Intel VRR
# Range Fix 3.0.0 (scripts\MSI-Claw-VRR-Fix.ps1 and
# scripts\Intel-VRR-LFC-Driver-Interface.ps1).
# Copyright (c) 2026 ClawLab contributors. MIT License, see LICENSE-ClawLab.txt.
#
#   ClawLab.VrrFix.ArcSyncControl      Intel ControlLib Arc Sync query/profile setter
#   ClawLab.IntelVrr.DirectVrrEscape   Intel private D3DKMT VRR escape (LFC flags)

$arcSyncSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClawLab.VrrFix
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CtlApplicationId
    {
        public UInt32 Data1;
        public UInt16 Data2;
        public UInt16 Data3;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data4;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CtlInitArgs
    {
        public UInt32 Size;
        public byte Version;
        public UInt32 AppVersion;
        public UInt32 Flags;
        public UInt32 SupportedVersion;
        public CtlApplicationId ApplicationUid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ArcSyncMonitorParams
    {
        public UInt32 Size;
        public byte Version;
        [MarshalAs(UnmanagedType.U1)] public bool IsSupported;
        public float MinimumRefreshRateInHz;
        public float MaximumRefreshRateInHz;
        public UInt32 MaxFrameTimeIncreaseInUs;
        public UInt32 MaxFrameTimeDecreaseInUs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ArcSyncProfileParams
    {
        public UInt32 Size;
        public byte Version;
        public Int32 Profile;
        public float MaxRefreshRateInHz;
        public float MinRefreshRateInHz;
        public UInt32 MaxFrameTimeIncreaseInUs;
        public UInt32 MaxFrameTimeDecreaseInUs;
    }

    public sealed class ArcSyncSnapshot
    {
        public int AdapterIndex { get; set; }
        public int DisplayIndex { get; set; }
        public int MonitorResult { get; set; }
        public bool Supported { get; set; }
        public float MonitorMinimumHz { get; set; }
        public float MonitorMaximumHz { get; set; }
        public UInt32 MonitorMaxIncreaseUs { get; set; }
        public UInt32 MonitorMaxDecreaseUs { get; set; }
        public int ProfileResult { get; set; }
        public int ProfileId { get; set; }
        public string ProfileName { get; set; }
        public float ActiveMinimumHz { get; set; }
        public float ActiveMaximumHz { get; set; }
        public UInt32 ActiveMaxIncreaseUs { get; set; }
        public UInt32 ActiveMaxDecreaseUs { get; set; }
    }

    public static class ArcSyncControl
    {
        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlInit(ref CtlInitArgs args, out IntPtr api);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlClose(IntPtr api);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlEnumerateDevices(IntPtr api, ref UInt32 count, IntPtr devices);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlEnumerateDisplayOutputs(IntPtr adapter, ref UInt32 count, IntPtr displays);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlGetIntelArcSyncInfoForMonitor(IntPtr display, ref ArcSyncMonitorParams parameters);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlGetIntelArcSyncProfile(IntPtr display, ref ArcSyncProfileParams parameters);

        [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ctlSetIntelArcSyncProfile(IntPtr display, ref ArcSyncProfileParams parameters);

        private static CtlInitArgs CreateInitArgs()
        {
            return new CtlInitArgs
            {
                Size = (UInt32)Marshal.SizeOf(typeof(CtlInitArgs)),
                Version = 0,
                AppVersion = 0x00010001,
                Flags = 0,
                ApplicationUid = new CtlApplicationId { Data4 = new byte[8] }
            };
        }

        private static string GetProfileName(int profile)
        {
            string[] names = { "INVALID", "RECOMMENDED", "EXCELLENT", "GOOD", "COMPATIBLE", "OFF", "VESA", "CUSTOM" };
            return profile >= 0 && profile < names.Length ? names[profile] : "UNKNOWN_" + profile;
        }

        public static ArcSyncSnapshot[] Query()
        {
            List<ArcSyncSnapshot> results = new List<ArcSyncSnapshot>();
            CtlInitArgs init = CreateInitArgs();
            IntPtr api;
            int result = ctlInit(ref init, out api);
            if (result != 0)
                throw new InvalidOperationException("ctlInit failed: 0x" + result.ToString("X8"));

            IntPtr adapters = IntPtr.Zero;
            try
            {
                UInt32 adapterCount = 0;
                result = ctlEnumerateDevices(api, ref adapterCount, IntPtr.Zero);
                if (result != 0)
                    throw new InvalidOperationException("ctlEnumerateDevices(count) failed: 0x" + result.ToString("X8"));

                adapters = Marshal.AllocHGlobal(checked((int)adapterCount * IntPtr.Size));
                result = ctlEnumerateDevices(api, ref adapterCount, adapters);
                if (result != 0)
                    throw new InvalidOperationException("ctlEnumerateDevices(list) failed: 0x" + result.ToString("X8"));

                for (int adapterIndex = 0; adapterIndex < adapterCount; adapterIndex++)
                {
                    IntPtr adapter = Marshal.ReadIntPtr(adapters, adapterIndex * IntPtr.Size);
                    UInt32 displayCount = 0;
                    result = ctlEnumerateDisplayOutputs(adapter, ref displayCount, IntPtr.Zero);
                    if (result != 0 || displayCount == 0)
                        continue;

                    IntPtr displays = Marshal.AllocHGlobal(checked((int)displayCount * IntPtr.Size));
                    try
                    {
                        result = ctlEnumerateDisplayOutputs(adapter, ref displayCount, displays);
                        if (result != 0)
                            continue;

                        for (int displayIndex = 0; displayIndex < displayCount; displayIndex++)
                        {
                            IntPtr display = Marshal.ReadIntPtr(displays, displayIndex * IntPtr.Size);
                            ArcSyncMonitorParams monitor = new ArcSyncMonitorParams
                            {
                                Size = (UInt32)Marshal.SizeOf(typeof(ArcSyncMonitorParams)),
                                Version = 0
                            };
                            int monitorResult = ctlGetIntelArcSyncInfoForMonitor(display, ref monitor);

                            ArcSyncProfileParams profile = new ArcSyncProfileParams
                            {
                                Size = (UInt32)Marshal.SizeOf(typeof(ArcSyncProfileParams)),
                                Version = 0
                            };
                            int profileResult = ctlGetIntelArcSyncProfile(display, ref profile);

                            results.Add(new ArcSyncSnapshot
                            {
                                AdapterIndex = adapterIndex,
                                DisplayIndex = displayIndex,
                                MonitorResult = monitorResult,
                                Supported = monitor.IsSupported,
                                MonitorMinimumHz = monitor.MinimumRefreshRateInHz,
                                MonitorMaximumHz = monitor.MaximumRefreshRateInHz,
                                MonitorMaxIncreaseUs = monitor.MaxFrameTimeIncreaseInUs,
                                MonitorMaxDecreaseUs = monitor.MaxFrameTimeDecreaseInUs,
                                ProfileResult = profileResult,
                                ProfileId = profile.Profile,
                                ProfileName = GetProfileName(profile.Profile),
                                ActiveMinimumHz = profile.MinRefreshRateInHz,
                                ActiveMaximumHz = profile.MaxRefreshRateInHz,
                                ActiveMaxIncreaseUs = profile.MaxFrameTimeIncreaseInUs,
                                ActiveMaxDecreaseUs = profile.MaxFrameTimeDecreaseInUs
                            });
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(displays);
                    }
                }
            }
            finally
            {
                if (adapters != IntPtr.Zero)
                    Marshal.FreeHGlobal(adapters);
                ctlClose(api);
            }
            return results.ToArray();
        }

        public static int SetProfile(int targetAdapterIndex, int targetDisplayIndex,
            float expectedMonitorMinimumHz, float expectedMonitorMaximumHz, int profileId,
            float minimumHz, float maximumHz, UInt32 maxIncreaseUs, UInt32 maxDecreaseUs)
        {
            CtlInitArgs init = CreateInitArgs();
            IntPtr api;
            int result = ctlInit(ref init, out api);
            if (result != 0)
                return result;

            IntPtr adapters = IntPtr.Zero;
            try
            {
                UInt32 adapterCount = 0;
                result = ctlEnumerateDevices(api, ref adapterCount, IntPtr.Zero);
                if (result != 0)
                    return result;
                if (targetAdapterIndex < 0 || targetAdapterIndex >= adapterCount)
                    return -1001;

                adapters = Marshal.AllocHGlobal(checked((int)adapterCount * IntPtr.Size));
                result = ctlEnumerateDevices(api, ref adapterCount, adapters);
                if (result != 0)
                    return result;

                IntPtr adapter = Marshal.ReadIntPtr(adapters, targetAdapterIndex * IntPtr.Size);
                UInt32 displayCount = 0;
                result = ctlEnumerateDisplayOutputs(adapter, ref displayCount, IntPtr.Zero);
                if (result != 0)
                    return result;
                if (targetDisplayIndex < 0 || targetDisplayIndex >= displayCount)
                    return -1002;

                IntPtr displays = Marshal.AllocHGlobal(checked((int)displayCount * IntPtr.Size));
                try
                {
                    result = ctlEnumerateDisplayOutputs(adapter, ref displayCount, displays);
                    if (result != 0)
                        return result;

                    IntPtr display = Marshal.ReadIntPtr(displays, targetDisplayIndex * IntPtr.Size);
                    ArcSyncMonitorParams monitor = new ArcSyncMonitorParams
                    {
                        Size = (UInt32)Marshal.SizeOf(typeof(ArcSyncMonitorParams)),
                        Version = 0
                    };
                    result = ctlGetIntelArcSyncInfoForMonitor(display, ref monitor);
                    if (result != 0 || !monitor.IsSupported)
                        return result != 0 ? result : -1003;
                    if (Math.Abs(monitor.MinimumRefreshRateInHz - expectedMonitorMinimumHz) > 0.1f ||
                        Math.Abs(monitor.MaximumRefreshRateInHz - expectedMonitorMaximumHz) > 0.1f)
                        return -1004;

                    ArcSyncProfileParams profile = new ArcSyncProfileParams
                    {
                        Size = (UInt32)Marshal.SizeOf(typeof(ArcSyncProfileParams)),
                        Version = 0,
                        Profile = profileId,
                        MinRefreshRateInHz = minimumHz,
                        MaxRefreshRateInHz = maximumHz,
                        MaxFrameTimeIncreaseInUs = maxIncreaseUs,
                        MaxFrameTimeDecreaseInUs = maxDecreaseUs
                    };
                    return ctlSetIntelArcSyncProfile(display, ref profile);
                }
                finally
                {
                    Marshal.FreeHGlobal(displays);
                }
            }
            finally
            {
                if (adapters != IntPtr.Zero)
                    Marshal.FreeHGlobal(adapters);
                ctlClose(api);
            }
        }
    }
}
'@

$vrrEscapeSource = @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClawLab.IntelVrr
{
    public sealed class VrrState
    {
        public UInt32 NtStatus { get; set; }
        public bool Supported { get; set; }
        public bool Enabled { get; set; }
        public bool HighFpsSolutionEnabled { get; set; }
        public bool LowFpsSolutionEnabled { get; set; }
        public UInt32 DisplayCount { get; set; }
        public UInt32 TargetId { get; set; }
        public UInt32 MinimumHz { get; set; }
        public UInt32 MaximumHz { get; set; }
        public string DisplayDeviceName { get; set; }
    }

    public static class DirectVrrEscape
    {
        private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        private const UInt32 D3DKMT_ESCAPE_DRIVERPRIVATE = 0;
        private const int PrivateDataSize = 124;
        private const int HeaderSize = 16;
        private const UInt32 DisplayEscapeMajor = 1;
        private const UInt32 VrrEscapeMinor = 10;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public UInt32 LowPart;
            public Int32 HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_OPENADAPTERFROMHDC
        {
            public IntPtr hDc;
            public UInt32 hAdapter;
            public LUID AdapterLuid;
            public UInt32 VidPnSourceId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_CLOSEADAPTER
        {
            public UInt32 hAdapter;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ESCAPE
        {
            public UInt32 hAdapter;
            public UInt32 hDevice;
            public UInt32 Type;
            public UInt32 Flags;
            public IntPtr pPrivateDriverData;
            public UInt32 PrivateDriverDataSize;
            public UInt32 hContext;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumDisplayDevices(
            string lpDevice,
            int iDevNum,
            ref DISPLAY_DEVICE lpDisplayDevice,
            int dwFlags);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDC(
            string lpszDriver,
            string lpszDevice,
            string lpszOutput,
            IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int D3DKMTOpenAdapterFromHdc(ref D3DKMT_OPENADAPTERFROMHDC data);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER data);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int D3DKMTEscape(ref D3DKMT_ESCAPE data);

        private static string GetSingleActiveIntelDisplay()
        {
            var matches = new List<string>();
            for (int index = 0; ; index++)
            {
                var device = new DISPLAY_DEVICE();
                device.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(null, index, ref device, 0))
                {
                    break;
                }
                if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                {
                    continue;
                }
                if (String.IsNullOrEmpty(device.DeviceID) ||
                    device.DeviceID.IndexOf("VEN_8086", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                matches.Add(device.DeviceName);
            }
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one active Intel display adapter; found " + matches.Count + ".");
            }
            return matches[0];
        }

        private static void WriteUInt32(byte[] data, int offset, UInt32 value)
        {
            byte[] raw = BitConverter.GetBytes(value);
            Buffer.BlockCopy(raw, 0, data, offset, raw.Length);
        }

        private static UInt32 ReadUInt32(byte[] data, int offset)
        {
            return BitConverter.ToUInt32(data, offset);
        }

        public static VrrState Invoke(UInt32 operation)
        {
            string displayName = GetSingleActiveIntelDisplay();
            IntPtr hdc = IntPtr.Zero;
            UInt32 adapterHandle = 0;
            IntPtr privateData = IntPtr.Zero;
            try
            {
                hdc = CreateDC(null, displayName, null, IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDC failed.");
                }

                var open = new D3DKMT_OPENADAPTERFROMHDC();
                open.hDc = hdc;
                int openStatus = D3DKMTOpenAdapterFromHdc(ref open);
                if (openStatus != 0 || open.hAdapter == 0)
                {
                    throw new InvalidOperationException(
                        "D3DKMTOpenAdapterFromHdc failed: 0x" + ((UInt32)openStatus).ToString("X8"));
                }
                adapterHandle = open.hAdapter;

                var payload = new byte[PrivateDataSize];
                WriteUInt32(payload, 8, DisplayEscapeMajor);
                WriteUInt32(payload, 12, VrrEscapeMinor);
                WriteUInt32(payload, HeaderSize, operation);

                privateData = Marshal.AllocHGlobal(payload.Length);
                Marshal.Copy(payload, 0, privateData, payload.Length);
                var escape = new D3DKMT_ESCAPE();
                escape.hAdapter = adapterHandle;
                escape.Type = D3DKMT_ESCAPE_DRIVERPRIVATE;
                escape.pPrivateDriverData = privateData;
                escape.PrivateDriverDataSize = (UInt32)payload.Length;

                int status = D3DKMTEscape(ref escape);
                UInt32 ntStatus = unchecked((UInt32)status);
                Marshal.Copy(privateData, payload, 0, payload.Length);

                var state = new VrrState();
                state.NtStatus = ntStatus;
                state.DisplayDeviceName = displayName;
                if (ntStatus != 0)
                {
                    return state;
                }
                state.Supported = payload[20] == 1;
                state.Enabled = payload[21] == 1;
                state.HighFpsSolutionEnabled = payload[22] == 1;
                state.LowFpsSolutionEnabled = payload[23] == 1;
                state.DisplayCount = ReadUInt32(payload, 24);
                if (state.DisplayCount > 0)
                {
                    state.TargetId = ReadUInt32(payload, 28);
                    state.MinimumHz = ReadUInt32(payload, 32);
                    state.MaximumHz = ReadUInt32(payload, 36);
                }
                return state;
            }
            finally
            {
                if (privateData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(privateData);
                }
                if (adapterHandle != 0)
                {
                    var close = new D3DKMT_CLOSEADAPTER();
                    close.hAdapter = adapterHandle;
                    D3DKMTCloseAdapter(ref close);
                }
                if (hdc != IntPtr.Zero)
                {
                    DeleteDC(hdc);
                }
            }
        }
    }
}
'@

function Initialize-IntelVrrInterop {
    if (-not ('ClawLab.VrrFix.ArcSyncControl' -as [type])) {
        $controlLibrary = Join-Path $env:SystemRoot 'System32\ControlLib.dll'
        if (-not (Test-Path -LiteralPath $controlLibrary -PathType Leaf)) {
            throw 'Intel Control Library (ControlLib.dll) is missing. Install a current Intel graphics driver first.'
        }
        Add-Type -TypeDefinition $arcSyncSource -Language CSharp
    }
    if (-not ('ClawLab.IntelVrr.DirectVrrEscape' -as [type])) {
        Add-Type -TypeDefinition $vrrEscapeSource -Language CSharp
    }
}
