using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GhostMic;

internal static class InstanceControl
{
    internal const int ShowMessage = 0x8030, QuitMessage = 0x8031, StatusMessage = 0x8032;
    internal static bool Send(int message)
    {
        bool sent = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint id);
            if (id == Environment.ProcessId) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            if (title.ToString() != "Ghost Mic") return true;
            try
            {
                using var process = Process.GetProcessById((int)id);
                if (process.ProcessName == "GhostMic") sent |= PostMessage(window, message, IntPtr.Zero, IntPtr.Zero);
            }
            catch (ArgumentException) { }
            return true;
        }, IntPtr.Zero);
        return sent;
    }
    private delegate bool Callback(IntPtr window, IntPtr argument);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr argument);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
}
