[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Initialize-SharpProofJobObjectInterop {
    if ('SharpProof.JobObjectNative' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpProof
{
    public static class JobObjectNative
    {
        public const int JobObjectExtendedLimitInformation = 9;
        public const uint CreateSuspended = 0x00000004;

        [Flags]
        public enum JobObjectLimitFlags : uint
        {
            JobMemory = 0x00000200,
            KillOnJobClose = 0x00002000
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public JobObjectLimitFlags LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
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
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr hJob,
            int jobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
'@
}

function ConvertTo-WindowsCommandLineArgument {
    param(
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($null -eq $Argument) {
        return '""'
    }

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument.IndexOfAny([char[]]@(' ', "`t", '"')) -lt 0) {
        return $Argument
    }

    $escaped = $Argument -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function ConvertTo-SignedProcessExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [uint32]$ExitCode
    )

    return [BitConverter]::ToInt32([BitConverter]::GetBytes($ExitCode), 0)
}

function Invoke-ProcessUnderJobObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$ArgumentList = @(),

        [ValidateRange(0, 1048576)]
        [int]$MemoryLimitMb = 0,

        [ValidateRange(0, 86400)]
        [int]$TimeoutSeconds = 0,

        [string]$WorkingDirectory = (Get-Location).Path
    )

    Initialize-SharpProofJobObjectInterop
    $resolvedFilePath = $FilePath
    $resolvedCommand = Get-Command -CommandType Application -Name $FilePath -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $resolvedCommand -and -not [string]::IsNullOrWhiteSpace($resolvedCommand.Source)) {
        $resolvedFilePath = $resolvedCommand.Source
    }
    elseif (Test-Path -LiteralPath $FilePath) {
        $resolvedFilePath = (Resolve-Path -LiteralPath $FilePath).Path
    }

    $jobHandle = [SharpProof.JobObjectNative]::CreateJobObject([IntPtr]::Zero, $null)
    if ($jobHandle -eq [IntPtr]::Zero) {
        $win32Error = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "CreateJobObject failed with Win32 error $win32Error."
    }

    $limitInfo = New-Object SharpProof.JobObjectNative+JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    $limitInfo.BasicLimitInformation.LimitFlags = [SharpProof.JobObjectNative+JobObjectLimitFlags]::KillOnJobClose
    if ($MemoryLimitMb -gt 0) {
        $limitInfo.BasicLimitInformation.LimitFlags = $limitInfo.BasicLimitInformation.LimitFlags -bor [SharpProof.JobObjectNative+JobObjectLimitFlags]::JobMemory
        $limitInfo.JobMemoryLimit = [UIntPtr]([uint64]$MemoryLimitMb * 1MB)
    }

    $limitInfoSize = [uint32][Runtime.InteropServices.Marshal]::SizeOf([type][SharpProof.JobObjectNative+JOBOBJECT_EXTENDED_LIMIT_INFORMATION])
    $limitInfoBuffer = [Runtime.InteropServices.Marshal]::AllocHGlobal([int]$limitInfoSize)

    $process = $null
    $processInformation = New-Object SharpProof.JobObjectNative+PROCESS_INFORMATION
    $processHandleOwned = $false
    $threadHandleOwned = $false
    try {
        [Runtime.InteropServices.Marshal]::StructureToPtr($limitInfo, $limitInfoBuffer, $false)
        if (-not [SharpProof.JobObjectNative]::SetInformationJobObject(
                $jobHandle,
                [SharpProof.JobObjectNative]::JobObjectExtendedLimitInformation,
                $limitInfoBuffer,
                $limitInfoSize)) {
            $win32Error = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "SetInformationJobObject failed with Win32 error $win32Error."
        }

        $commandLine = ((@($resolvedFilePath) + $ArgumentList | ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join ' ')
        $commandLineBuilder = [System.Text.StringBuilder]::new($commandLine)
        $startupInfo = New-Object SharpProof.JobObjectNative+STARTUPINFO
        $startupInfo.cb = [uint32][Runtime.InteropServices.Marshal]::SizeOf([type][SharpProof.JobObjectNative+STARTUPINFO])
        if (-not [SharpProof.JobObjectNative]::CreateProcess(
                $resolvedFilePath,
                $commandLineBuilder,
                [IntPtr]::Zero,
                [IntPtr]::Zero,
                $true,
                [SharpProof.JobObjectNative]::CreateSuspended,
                [IntPtr]::Zero,
                $WorkingDirectory,
                [ref]$startupInfo,
                [ref]$processInformation)) {
            $win32Error = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "CreateProcess failed for '$FilePath' with Win32 error $win32Error."
        }

        $processHandleOwned = $true
        $threadHandleOwned = $true
        if (-not [SharpProof.JobObjectNative]::AssignProcessToJobObject($jobHandle, $processInformation.hProcess)) {
            $win32Error = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            [void][SharpProof.JobObjectNative]::TerminateJobObject($jobHandle, 124)
            throw "AssignProcessToJobObject failed with Win32 error $win32Error."
        }

        $process = [System.Diagnostics.Process]::GetProcessById([int]$processInformation.dwProcessId)
        $resumeResult = [SharpProof.JobObjectNative]::ResumeThread($processInformation.hThread)
        if ($resumeResult -eq [uint32]::MaxValue) {
            $win32Error = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            [void][SharpProof.JobObjectNative]::TerminateJobObject($jobHandle, 124)
            throw "ResumeThread failed with Win32 error $win32Error."
        }

        if ($TimeoutSeconds -gt 0) {
            $timeoutMilliseconds = $TimeoutSeconds * 1000
            if (-not $process.WaitForExit($timeoutMilliseconds)) {
                [void][SharpProof.JobObjectNative]::TerminateJobObject($jobHandle, 124)
                $process.WaitForExit()
                $global:LASTEXITCODE = 124
                return 124
            }
        }
        else {
            $process.WaitForExit()
        }

        $nativeExitCode = [uint32]0
        if (-not [SharpProof.JobObjectNative]::GetExitCodeProcess($processInformation.hProcess, [ref]$nativeExitCode)) {
            $win32Error = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "GetExitCodeProcess failed with Win32 error $win32Error."
        }

        $exitCode = ConvertTo-SignedProcessExitCode -ExitCode $nativeExitCode
        $global:LASTEXITCODE = $exitCode
        return $exitCode
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            [void][SharpProof.JobObjectNative]::TerminateJobObject($jobHandle, 124)
        }

        [Runtime.InteropServices.Marshal]::FreeHGlobal($limitInfoBuffer)
        if ($threadHandleOwned) {
            [void][SharpProof.JobObjectNative]::CloseHandle($processInformation.hThread)
        }

        if ($processHandleOwned) {
            [void][SharpProof.JobObjectNative]::CloseHandle($processInformation.hProcess)
        }

        [void][SharpProof.JobObjectNative]::CloseHandle($jobHandle)
    }
}
