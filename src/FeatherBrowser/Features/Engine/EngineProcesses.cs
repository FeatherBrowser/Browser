using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FeatherBrowser.Features.Engine;

internal static class EngineProcesses
{
    public static IReadOnlyDictionary<int, string> Snapshot()
    {
        var result = new Dictionary<int, string> { [Environment.ProcessId] = "Feather UI + browser" };
        if (!OperatingSystem.IsWindows())
            return result;

        Dictionary<int, int> parents = ReadParentMap();
        var descendants = new HashSet<int> { Environment.ProcessId };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach ((int pid, int parent) in parents)
            {
                if (descendants.Contains(parent) && descendants.Add(pid))
                    grew = true;
            }
        }

        foreach (int pid in descendants.Where(pid => pid != Environment.ProcessId))
            result[pid] = Describe(ReadCommandLine(pid));

        return result;
    }

    internal static string Describe(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return "Chromium helper";

        string type = Switch(commandLine, "--type=");
        return type switch
        {
            "renderer" => commandLine.Contains("--extension-process", StringComparison.Ordinal) ? "Extension" : "Renderer",
            "gpu-process" => "GPU",
            "crashpad-handler" => "Crash reporter",
            "utility" => Switch(commandLine, "--utility-sub-type=") switch
            {
                "network.mojom.NetworkService" => "Network service",
                "storage.mojom.StorageService" => "Storage service",
                "audio.mojom.AudioService" => "Audio service",
                "video_capture.mojom.VideoCaptureService" => "Video capture",
                "data_decoder.mojom.DataDecoderService" => "Data decoder",
                { Length: > 0 } other => "Utility · " + other.Split('.')[0],
                _ => "Utility"
            },
            { Length: > 0 } other => other,
            _ => "Chromium helper"
        };
    }

    private static string Switch(string commandLine, string name)
    {
        int start = commandLine.IndexOf(name, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += name.Length;
        int end = commandLine.IndexOfAny([' ', '"'], start);
        return end < 0 ? commandLine[start..] : commandLine[start..end];
    }

    private static Dictionary<int, int> ReadParentMap()
    {
        var map = new Dictionary<int, int>();
        using SafeFileHandle snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32FirstW(snapshot, ref entry))
            return map;

        do
        {
            map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
        }
        while (Process32NextW(snapshot, ref entry));

        return map;
    }

    private static string? ReadCommandLine(int pid)
    {
        using SafeProcessHandle process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process.IsInvalid)
            return null;

        int size = 0;
        NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, ref size);
        if (size <= 0 || size > 1 << 20)
            return null;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, size, ref size) != 0)
                return null;

            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return text.Buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const uint SnapProcess = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, IntPtr information, int length, ref int returnLength);
}
