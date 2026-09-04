using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace StarshipTitanicAp;

/// <summary>Thin wrapper around the Win32 ReadProcessMemory/WriteProcessMemory APIs.</summary>
public sealed class MemoryReader : IDisposable
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_CREATE_THREAD = 0x0002;

    private const uint FullAccess = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
        | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_CREATE_THREAD;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesWritten);

    private IntPtr _processHandle = IntPtr.Zero;

    public bool IsAttached { get; private set; }
    public long ModuleBase { get; private set; }
    public long ModuleSize { get; private set; }
    public int ProcessId { get; private set; }
    internal IntPtr ProcessHandle => _processHandle;

    /// <summary>True if addr falls within the attached module's own mapped range.</summary>
    public bool IsWithinModule(long addr) =>
        IsAttached && addr >= ModuleBase && addr < ModuleBase + ModuleSize;

    /// <summary>Attempts to attach to the named process. Returns true on success.</summary>
    public bool Attach(string processName)
    {
        Detach();

        Process[] candidates = Process.GetProcessesByName(processName);
        if (candidates.Length == 0)
            return false;

        Process process = candidates[0];

        try
        {
            _processHandle = OpenProcess(FullAccess, false, process.Id);
            if (_processHandle == IntPtr.Zero)
                return false;

            ModuleBase = process.MainModule?.BaseAddress.ToInt64() ?? 0;
            ModuleSize = process.MainModule?.ModuleMemorySize ?? 0;
            if (ModuleBase == 0)
            {
                Detach();
                return false;
            }

            ProcessId = process.Id;
            IsAttached = true;
            return true;
        }
        catch
        {
            Detach();
            return false;
        }
        finally
        {
            foreach (Process p in candidates)
                p.Dispose();
        }
    }

    public void Detach()
    {
        if (_processHandle != IntPtr.Zero)
        {
            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
        IsAttached = false;
        ModuleBase = 0;
        ModuleSize = 0;
        ProcessId = 0;
    }

    /// <summary>Reads an 8-byte little-endian value (a pointer/qword). Null on failure.</summary>
    public long? ReadInt64(long address)
    {
        byte[]? buf = ReadBytes(address, 8);
        return buf is null ? null : BitConverter.ToInt64(buf, 0);
    }

    /// <summary>Reads a 4-byte little-endian value (an int/dword). Null on failure.</summary>
    public int? ReadInt32(long address)
    {
        byte[]? buf = ReadBytes(address, 4);
        return buf is null ? null : BitConverter.ToInt32(buf, 0);
    }

    public byte[]? ReadBytes(long address, int count)
    {
        if (!IsAttached || address <= 0)
            return null;

        byte[] buffer = new byte[count];
        bool ok = ReadProcessMemory(_processHandle, (IntPtr)address, buffer, count, out IntPtr bytesRead);
        if (!ok || bytesRead.ToInt64() != count)
            return null;

        return buffer;
    }

    /// <summary>Writes a 4-byte little-endian value. Returns true on success.</summary>
    public bool WriteInt32(long address, int value) =>
        WriteBytes(address, BitConverter.GetBytes(value));

    /// <summary>Writes an 8-byte little-endian value (a pointer/qword). Returns true on success.</summary>
    public bool WriteInt64(long address, long value) =>
        WriteBytes(address, BitConverter.GetBytes(value));

    /// <summary>Writes a single byte. Returns true on success.</summary>
    public bool WriteByte(long address, byte value) =>
        WriteBytes(address, new[] { value });

    public bool WriteBytes(long address, byte[] data)
    {
        if (!IsAttached || address <= 0)
            return false;

        bool ok = WriteProcessMemory(_processHandle, (IntPtr)address, data, data.Length, out IntPtr bytesWritten);
        return ok && bytesWritten.ToInt64() == data.Length;
    }

    /// <summary>Reads a short run of printable ASCII starting at address, stopping at the first non-printable byte.</summary>
    public string? ReadShortAsciiString(long address, int maxLength = 32)
    {
        byte[]? raw = ReadBytes(address, maxLength);
        if (raw is null)
            return null;

        var sb = new StringBuilder();
        foreach (byte b in raw)
        {
            if (b is >= 32 and < 127)
                sb.Append((char)b);
            else
                break;
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    public void Dispose() => Detach();
}
