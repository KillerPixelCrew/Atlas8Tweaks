using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Atlas8PowerButton
{
    internal static class Native
    {
        public const int HidpStatusSuccess = 0x00110000;
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareReadWrite = 3;
        public const uint OpenExisting = 3;

        public const int WmQueryEndSession = 0x0011;
        public const int WmEndSession = 0x0016;
        public const int WmPowerBroadcast = 0x0218;
        public const int WmSysCommand = 0x0112;
        public const int ScMonitorPower = 0xF170;
        public const int PbtApmSuspend = 0x0004;
        public const int PbtApmResumeSuspend = 0x0007;
        public const int PbtApmResumeAutomatic = 0x0012;
        public const int PbtPowerSettingChange = 0x8013;

        public static readonly Guid GuidDevInterfaceHid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        public static readonly Guid GuidConsoleDisplayState = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        public static readonly Guid GuidActivePowerScheme = new Guid("31f9f286-5084-42fe-b720-2b0264993763");
        public static readonly Guid GuidSubButtons = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
        public static readonly Guid GuidPowerButtonAction = new Guid("7648efa3-dd9c-4e3e-b566-50f929386280");

        [StructLayout(LayoutKind.Sequential)]
        public struct SpDeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PowerBroadcastSetting
        {
            public Guid PowerSetting;
            public uint DataLength;
            public uint Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint Type;
            public MouseInput Mouse;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int toRead, out int read, IntPtr overlapped);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

        [DllImport("kernel32")]
        public static extern uint SetThreadExecutionState(uint flags);

        [DllImport("kernel32")]
        public static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("setupapi", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid, uint index,
            ref SpDeviceInterfaceData data);

        [DllImport("setupapi", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data,
            IntPtr detail, uint size, out uint required, IntPtr info);

        [DllImport("setupapi")]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("hid")]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

        [DllImport("hid")]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [DllImport("hid")]
        public static extern int HidP_GetCaps(IntPtr preparsed, byte[] caps);

        [DllImport("hid")]
        public static extern int HidP_GetButtonCaps(int reportType, byte[] caps, ref ushort length, IntPtr preparsed);

        [DllImport("hid")]
        public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
            ushort[] usages, ref uint usageLength, IntPtr preparsed, byte[] report, uint reportLength);

        [DllImport("powrprof")]
        public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

        [DllImport("powrprof")]
        public static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

        [DllImport("powrprof")]
        public static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subGroup, uint access, uint index,
            byte[] buffer, ref uint size);

        [DllImport("powrprof")]
        public static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting,
            out uint value);

        [DllImport("powrprof")]
        public static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting,
            out uint value);

        [DllImport("powrprof")]
        public static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting,
            uint value);

        [DllImport("powrprof")]
        public static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting,
            uint value);

        [DllImport("powrprof", SetLastError = true)]
        public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("powrprof")]
        public static extern bool GetPwrCapabilities(byte[] capabilities);

        [DllImport("user32", SetLastError = true)]
        public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

        [DllImport("user32")]
        public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32")]
        public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("user32")]
        public static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32")]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32")]
        public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

        [DllImport("user32", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr desktop);

        public const uint TokenAllAccess = 0xF01FF;
        public const uint CreateNoWindow = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo
        {
            public int Size;
            public string Reserved;
            public string Desktop;
            public string Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, Reserved2;
            public IntPtr Reserved3, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public int ProcessId;
            public int ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Luid
        {
            public uint Low;
            public int High;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TokenPrivileges
        {
            public int Count;
            public Luid Luid;
            public int Attributes;
        }

        [DllImport("kernel32")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("kernel32")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32")]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32", SetLastError = true)]
        public static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonation,
            int type, out IntPtr duplicate);

        [DllImport("advapi32", SetLastError = true)]
        public static extern bool SetTokenInformation(IntPtr token, int infoClass, ref uint info, int length);

        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);

        [DllImport("advapi32", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state,
            int length, IntPtr previous, IntPtr returned);

        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcessAsUser(IntPtr token, string application, string commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment,
            string directory, ref StartupInfo startup, out ProcessInformation information);
    }
}
