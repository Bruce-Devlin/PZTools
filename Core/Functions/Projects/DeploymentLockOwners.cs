using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PZTools.Core.Functions.Projects;

internal static class DeploymentLockOwners
{
    internal static IReadOnlyList<string> Find(string directory)
    {
        uint handle = 0;
        try
        {
            if (!Directory.Exists(directory)) return new[] { "Destination does not exist; inspect the recorded phase and paths." };
            var files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).Take(512).ToArray();
            if (files.Length == 0) return new[] { "No files available for Windows Restart Manager inspection." };
            var key = new StringBuilder(33);
            if (RmStartSession(out handle, 0, key) != 0) return new[] { "Windows Restart Manager unavailable." };
            if (RmRegisterResources(handle, (uint)files.Length, files, 0, null, 0, null) != 0) return new[] { "Windows could not register the files for lock inspection." };
            uint needed = 0, count = 0, reason = 0;
            var result = RmGetList(handle, out needed, ref count, null, ref reason);
            if (result == 234)
            {
                var owners = new ProcessInfo[needed]; count = needed;
                if (RmGetList(handle, out needed, ref count, owners, ref reason) == 0)
                    return owners.Take((int)count).Select(p => $"{p.Name} (PID {p.Process.Id})").ToArray();
            }
            return new[] { "No lock owner identified among the first 512 files; directory handles and scanners may not be reported." };
        }
        catch (Exception ex) { return new[] { "Lock inspection unavailable: " + ex.Message }; }
        finally { if (handle != 0) RmEndSession(handle); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess { public uint Id; public System.Runtime.InteropServices.ComTypes.FILETIME Started; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public uint Type, Status, Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint handle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint handle, uint files, string[] names, uint processes, UniqueProcess[]? apps, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] ProcessInfo[]? owners, ref uint reason);
}
