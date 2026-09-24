using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     A Windows job object that ends every process in it when its last handle closes. Windows closes the handle when
///     the editor exits for any reason, a crash included, so a server a live test started cannot outlive the editor.
/// </summary>
internal sealed class KillOnCloseJob : IDisposable
{
    private const int ExtendedLimitInformationClass = 9;
    private const uint KillOnJobCloseFlag = 0x2000;

    private IntPtr m_handle;

    private KillOnCloseJob(IntPtr handle)
    {
        m_handle = handle;
    }

    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public void Dispose()
    {
        if (m_handle != IntPtr.Zero)
        {
            CloseHandle(m_handle);
            m_handle = IntPtr.Zero;
        }
    }

    public static KillOnCloseJob Create()
    {
        IntPtr handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobCloseFlag } };
        int length = Marshal.SizeOf<ExtendedLimits>();
        if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, ref limits, length))
        {
            int error = Marshal.GetLastWin32Error();
            CloseHandle(handle);
            throw new Win32Exception(error);
        }

        return new KillOnCloseJob(handle);
    }

    public void Add(Process process)
    {
        if (!AssignProcessToJobObject(m_handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public bool Contains(Process process)
    {
        return IsProcessInJob(process.Handle, m_handle, out bool isInJob) && isInJob;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        ref ExtendedLimits information,
        int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    // JOBOBJECT_BASIC_LIMIT_INFORMATION.
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    // IO_COUNTERS.
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION.
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
}
