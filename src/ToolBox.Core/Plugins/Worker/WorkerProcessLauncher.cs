using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ToolBox.Core.Lifetime;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ToolBox.Core.Plugins.Worker;

[SupportedOSPlatform("windows")]
public sealed class WorkerProcessLauncher
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Keep process creation instance-based as the runtime boundary for future host services.")]
    [SuppressMessage("Usage", "CA1838:Avoid StringBuilder parameters for P/Invokes", Justification = "CreateProcess requires a mutable command line buffer on the Windows prototype boundary.")]
    public WorkerProcessHandle Start(
        string workerExecutablePath,
        string pluginDirectory,
        string pipeName,
        string launchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);

        if (!File.Exists(workerExecutablePath))
        {
            throw new FileNotFoundException(
                "The PluginWorker executable could not be found.",
                workerExecutablePath);
        }

        if (!Directory.Exists(pluginDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The OutOfProcess plugin directory could not be found: '{pluginDirectory}'.");
        }

        var startupInfo = new STARTUPINFO
        {
            cb = (uint)Marshal.SizeOf<STARTUPINFO>()
        };
        var commandLine = new StringBuilder(
            string.Join(
                " ",
                Quote(workerExecutablePath),
                "--pipe",
                Quote(pipeName),
                "--launch-id",
                Quote(launchId),
                "--plugin-directory",
                Quote(pluginDirectory)));

        if (!CreateProcess(
                workerExecutablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: false,
                CreateSuspended | CreateNoWindow,
                IntPtr.Zero,
                Path.GetDirectoryName(workerExecutablePath),
                ref startupInfo,
                out var processInformation))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The PluginWorker process could not be created suspended.");
        }

        using var processHandle = new SafeKernelHandle(processInformation.hProcess, ownsHandle: true);
        using var threadHandle = new SafeKernelHandle(processInformation.hThread, ownsHandle: true);
        WindowsJobObject? job = null;

        try
        {
            // The Worker is still suspended here. It is not resumed until the
            // Job policy and process-tree assignment have both succeeded.
            job = new WindowsJobObject();
            job.ConfigureKillOnClose();
            job.AssignProcess(processHandle);

            if (ResumeThread(threadHandle.DangerousGetHandle()) == uint.MaxValue)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The PluginWorker primary thread could not be resumed after Job assignment.");
            }

            var process = Process.GetProcessById(processInformation.dwProcessId);
            return new WorkerProcessHandle(process, job);
        }
        catch
        {
            if (job is not null)
            {
                try
                {
                    job.Terminate();
                }
                finally
                {
                    job.Dispose();
                }
            }
            else
            {
                TerminateProcess(processHandle.DangerousGetHandle(), 1);
            }

            throw;
        }
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var backslashCount = 0;

        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            builder.Append('\\', backslashCount);
            builder.Append(character);
            backslashCount = 0;
        }

        builder.Append('\\', backslashCount * 2);
        builder.Append('"');
        return builder.ToString();
    }

    [SuppressMessage("Usage", "CA1838:Avoid StringBuilder parameters for P/Invokes", Justification = "CreateProcess requires a mutable command line buffer on the Windows prototype boundary.")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [SupportedOSPlatform("windows")]
    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle(IntPtr preexistingHandle, bool ownsHandle)
            : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}

[SupportedOSPlatform("windows")]
public sealed class WorkerProcessHandle : IDisposable
{
    private readonly WindowsJobObject _job;
    private bool _disposed;

    internal WorkerProcessHandle(Process process, WindowsJobObject job)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        _job = job ?? throw new ArgumentNullException(nameof(job));
    }

    public Process Process { get; }

    public int ProcessId => Process.Id;

    public bool HasExited => Process.HasExited;

    public void Terminate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _job.Terminate();
    }

    public void Dispose(ShutdownDeadline? deadline = null)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (!Process.HasExited)
            {
                _job.Terminate();
                if (deadline is not null)
                {
                    try
                    {
                        WaitForExit(deadline);
                    }
                    catch (OperationCanceledException)
                    {
                        // The process tree has already been terminated by the
                        // Job Object. Cleanup must still release native handles.
                    }
                }
            }
        }
        finally
        {
            _job.Dispose();
            Process.Dispose();
        }
    }

    public void Dispose() => Dispose(deadline: null);

    void IDisposable.Dispose() => Dispose(deadline: null);

    public void WaitForExit(ShutdownDeadline deadline)
    {
        ArgumentNullException.ThrowIfNull(deadline);

        while (!Process.HasExited)
        {
            deadline.ThrowIfExpired();
            Process.WaitForExit((int)Math.Clamp(
                Math.Ceiling(deadline.Remaining.TotalMilliseconds),
                1,
                25));
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsJobObject : IDisposable
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private SafeJobHandle? _handle;

    public WindowsJobObject()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The Windows Job Object could not be created.");
        }

        _handle = new SafeJobHandle(handle, ownsHandle: true);
    }

    public void ConfigureKillOnClose()
    {
        var information = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        if (!SetInformationJobObject(
                Handle,
                JobObjectExtendedLimitInformation,
                ref information,
                (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The PluginWorker Job Object policy could not be configured.");
        }
    }

    public void AssignProcess(SafeHandle processHandle)
    {
        ArgumentNullException.ThrowIfNull(processHandle);

        if (!AssignProcessToJobObject(Handle, processHandle.DangerousGetHandle()))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "The PluginWorker could not be assigned to its Job Object.");
        }
    }

    public void Terminate()
    {
        if (_handle is null || _handle.IsInvalid)
        {
            return;
        }

        if (!TerminateJobObject(Handle, 1))
        {
            var error = Marshal.GetLastWin32Error();

            if (error != 6)
            {
                throw new Win32Exception(error, "The PluginWorker Job Object could not be terminated.");
            }
        }
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private IntPtr Handle => _handle?.DangerousGetHandle() ?? IntPtr.Zero;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(
        IntPtr lpJobAttributes,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        uint jobObjectInformationClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle(IntPtr preexistingHandle, bool ownsHandle)
            : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
