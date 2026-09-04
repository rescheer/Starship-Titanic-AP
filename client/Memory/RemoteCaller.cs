using System.Runtime.InteropServices;

namespace StarshipTitanicAp;

/// <summary>Calls an existing function inside the target process by writing a small stub and executing it via CreateRemoteThread.</summary>
public static class RemoteCaller
{
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Allocates a small buffer in the target process and writes the given bytes into it. Returns the remote address, or 0 on failure.</summary>
    public static long AllocateAndWrite(MemoryReader mem, byte[] data)
    {
        long remoteMem = Allocate(mem, data.Length);
        if (remoteMem == 0)
            return 0;

        if (!mem.WriteBytes(remoteMem, data))
        {
            FreeRemoteMemory(mem, remoteMem);
            return 0;
        }

        return remoteMem;
    }

    /// <summary>Reserves+commits a remote buffer without writing to it yet - needed when a stub's own bytes (e.g.
    /// a relocated relative CALL/JMP) depend on knowing the stub's final address before it can be built.</summary>
    public static long Allocate(MemoryReader mem, int size)
    {
        if (!mem.IsAttached)
            return 0;

        IntPtr remoteMem = VirtualAllocEx(mem.ProcessHandle, IntPtr.Zero, (UIntPtr)size,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        return remoteMem.ToInt64();
    }

    public static void FreeRemoteMemory(MemoryReader mem, long address)
    {
        if (address != 0)
            VirtualFreeEx(mem.ProcessHandle, (IntPtr)address, UIntPtr.Zero, MEM_RELEASE);
    }

    /// <summary>Builds a small x64 stub that sets up the call arguments, calls funcAddr, then returns cleanly.</summary>
    private static byte[] BuildStub(long funcAddr, long rcx, long rdx, long r8, int r9d, int arg5, int arg6)
    {
        using var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b, 0, b.Length);
        void WQ(long v) => ms.Write(BitConverter.GetBytes(v), 0, 8);
        void WI(int v) => ms.Write(BitConverter.GetBytes(v), 0, 4);

        W(0x55);                               // push rbp
        W(0x48, 0x89, 0xE5);                   // mov rbp, rsp
        W(0x48, 0x83, 0xE4, 0xF0);             // and rsp, -16
        W(0x48, 0x83, 0xEC, 0x40);             // sub rsp, 0x40

        W(0x48, 0xB9); WQ(rcx);                // mov rcx, imm64
        W(0x48, 0xBA); WQ(rdx);                // mov rdx, imm64
        W(0x49, 0xB8); WQ(r8);                 // mov r8, imm64
        W(0x41, 0xB9); WI(r9d);                // mov r9d, imm32

        W(0xC7, 0x44, 0x24, 0x20); WI(arg5);   // mov dword [rsp+0x20], imm32
        W(0xC7, 0x44, 0x24, 0x28); WI(arg6);   // mov dword [rsp+0x28], imm32

        W(0x48, 0xB8); WQ(funcAddr);           // mov rax, imm64
        W(0xFF, 0xD0);                         // call rax

        W(0x48, 0x89, 0xEC);                   // mov rsp, rbp
        W(0x5D);                               // pop rbp
        W(0xC3);                               // ret

        return ms.ToArray();
    }

    /// <summary>Writes the stub into the target process and executes it via CreateRemoteThread, waiting for completion.</summary>
    public static bool Call(MemoryReader mem, long funcAddr,
        long rcx = 0, long rdx = 0, long r8 = 0, int r9d = 0, int arg5 = 0, int arg6 = 0)
    {
        if (!mem.IsAttached)
            return false;

        byte[] stub = BuildStub(funcAddr, rcx, rdx, r8, r9d, arg5, arg6);

        IntPtr remoteMem = VirtualAllocEx(mem.ProcessHandle, IntPtr.Zero, (UIntPtr)stub.Length,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        if (remoteMem == IntPtr.Zero)
            return false;

        bool wrote = WriteProcessMemory(mem.ProcessHandle, remoteMem, stub, (uint)stub.Length, out _);
        if (!wrote)
        {
            VirtualFreeEx(mem.ProcessHandle, remoteMem, UIntPtr.Zero, MEM_RELEASE);
            return false;
        }

        IntPtr hThread = CreateRemoteThread(mem.ProcessHandle, IntPtr.Zero, 0, remoteMem, IntPtr.Zero, 0, out _);
        if (hThread == IntPtr.Zero)
        {
            VirtualFreeEx(mem.ProcessHandle, remoteMem, UIntPtr.Zero, MEM_RELEASE);
            return false;
        }

        WaitForSingleObject(hThread, INFINITE);

        CloseHandle(hThread);
        VirtualFreeEx(mem.ProcessHandle, remoteMem, UIntPtr.Zero, MEM_RELEASE);

        return true;
    }

    /// <summary>Builds the same argument-loading stub as BuildStub, but captures the return value into a mailbox address.</summary>
    private static byte[] BuildStubWithCapture(long funcAddr, long mailboxAddr,
        long rcx, long rdx, long r8, int r9d, int arg5, int arg6)
    {
        using var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b, 0, b.Length);
        void WQ(long v) => ms.Write(BitConverter.GetBytes(v), 0, 8);
        void WI(int v) => ms.Write(BitConverter.GetBytes(v), 0, 4);

        W(0x55);                               // push rbp
        W(0x48, 0x89, 0xE5);                   // mov rbp, rsp
        W(0x48, 0x83, 0xE4, 0xF0);             // and rsp, -16
        W(0x48, 0x83, 0xEC, 0x40);             // sub rsp, 0x40

        W(0x48, 0xB9); WQ(rcx);                // mov rcx, imm64
        W(0x48, 0xBA); WQ(rdx);                // mov rdx, imm64
        W(0x49, 0xB8); WQ(r8);                 // mov r8, imm64
        W(0x41, 0xB9); WI(r9d);                // mov r9d, imm32

        W(0xC7, 0x44, 0x24, 0x20); WI(arg5);   // mov dword [rsp+0x20], imm32
        W(0xC7, 0x44, 0x24, 0x28); WI(arg6);   // mov dword [rsp+0x28], imm32

        W(0x48, 0xB8); WQ(funcAddr);           // mov rax, imm64
        W(0xFF, 0xD0);                         // call rax

        // Capture the return value before the epilogue can clobber it.
        W(0x49, 0xBA); WQ(mailboxAddr);        // mov r10, imm64 (mailbox address)
        W(0x49, 0x89, 0x02);                   // mov qword ptr [r10], rax

        W(0x48, 0x89, 0xEC);                   // mov rsp, rbp
        W(0x5D);                               // pop rbp
        W(0xC3);                               // ret

        return ms.ToArray();
    }

    /// <summary>Same calling convention as Call, but returns the callee's rax instead of discarding it. Null on failure.</summary>
    public static long? CallAndGetResult(MemoryReader mem, long funcAddr,
        long rcx = 0, long rdx = 0, long r8 = 0, int r9d = 0, int arg5 = 0, int arg6 = 0)
    {
        if (!mem.IsAttached)
            return null;

        byte[] mailboxInit = new byte[8]; // zeroed
        long mailboxAddr = AllocateAndWrite(mem, mailboxInit);
        if (mailboxAddr == 0)
            return null;

        byte[] stub = BuildStubWithCapture(funcAddr, mailboxAddr, rcx, rdx, r8, r9d, arg5, arg6);

        IntPtr remoteMem = VirtualAllocEx(mem.ProcessHandle, IntPtr.Zero, (UIntPtr)stub.Length,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        if (remoteMem == IntPtr.Zero)
        {
            FreeRemoteMemory(mem, mailboxAddr);
            return null;
        }

        bool wrote = WriteProcessMemory(mem.ProcessHandle, remoteMem, stub, (uint)stub.Length, out _);
        if (!wrote)
        {
            VirtualFreeEx(mem.ProcessHandle, remoteMem, UIntPtr.Zero, MEM_RELEASE);
            FreeRemoteMemory(mem, mailboxAddr);
            return null;
        }

        IntPtr hThread = CreateRemoteThread(mem.ProcessHandle, IntPtr.Zero, 0, remoteMem, IntPtr.Zero, 0, out _);
        if (hThread == IntPtr.Zero)
        {
            VirtualFreeEx(mem.ProcessHandle, remoteMem, UIntPtr.Zero, MEM_RELEASE);
            FreeRemoteMemory(mem, mailboxAddr);
            return null;
        }

        WaitForSingleObject(hThread, INFINITE);

        CloseHandle(hThread);
        VirtualFreeEx(mem.ProcessHandle, remoteMem, UIntPtr.Zero, MEM_RELEASE);

        long? result = mem.ReadInt64(mailboxAddr);
        FreeRemoteMemory(mem, mailboxAddr);
        return result;
    }
}
