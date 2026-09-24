using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

class QaRunner
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Startup
    {
        public int cb;
        public string reserved, desktop, title;
        public int x, y, w, h, cx, cy, fill, flags;
        public short show, reserved2;
        public IntPtr reserved3, input, output, error;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo
    {
        public IntPtr process, thread;
        public int processId, threadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, int flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo process);
    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll")]
    static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")]
    static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
    static int Main(string[] args)
    {
        if (args.Length < 1)
            return 2;
        var executable = Path.GetFullPath(args[0]);
        var name = "ST4RR-QA-" + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
        {
            Console.WriteLine("CreateDesktop failed: " + Marshal.GetLastWin32Error());
            return 3;
        }

        try
        {
            var start = new Startup
            {
                cb = Marshal.SizeOf(typeof(Startup)),
                desktop = "winsta0\\" + name,
                flags = 1,
                show = 0
            };
            ProcessInfo process;
            var command = new StringBuilder("\"" + executable + "\" --smoke" + (Array.IndexOf(args, "--en") >= 0 ? " --en" : ""));
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Path.GetDirectoryName(executable), ref start, out process))
            {
                Console.WriteLine("CreateProcess failed: " + Marshal.GetLastWin32Error());
                return 4;
            }

            try
            {
                if (WaitForSingleObject(process.process, 60000) != 0)
                {
                    TerminateProcess(process.process, 5);
                    Console.WriteLine("QA timed out on isolated desktop");
                    return 5;
                }

                uint code;
                GetExitCodeProcess(process.process, out code);
                Console.WriteLine("Isolated desktop QA exit: " + code);
                return (int)code;
            }
            finally
            {
                CloseHandle(process.thread);
                CloseHandle(process.process);
            }
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }
}
