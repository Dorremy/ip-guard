using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IpGuard;

public static class ProgramCloser
{
    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    public static string Normalize(string? name) => ProgramNames.Normalize(name);

    public static bool IsProtected(string name) => ProgramNames.IsProtected(name);

    public static IReadOnlyList<RunningProgram> RunningWithWindows()
    {
        var result = new Dictionary<string, RunningProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle) || IsProtected(process.ProcessName))
                {
                    continue;
                }

                result.TryAdd(process.ProcessName, new RunningProgram(process.ProcessName, process.MainWindowTitle));
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.Values.OrderBy(program => program.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static CloseReport Close(IEnumerable<string> names)
    {
        var asked = new List<string>();
        var notRunning = new List<string>();
        var noWindow = new List<string>();
        var skipped = new List<string>();

        foreach (var name in names.Select(Normalize).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsProtected(name))
            {
                skipped.Add(name);
                continue;
            }

            var processes = Process.GetProcessesByName(name);
            if (processes.Length == 0)
            {
                notRunning.Add(name);
                continue;
            }

            var posted = 0;
            foreach (var process in processes)
            {
                posted += AskToClose(process.Id);
                process.Dispose();
            }

            (posted > 0 ? asked : noWindow).Add(name);
        }

        return new CloseReport(asked, notRunning, noWindow, skipped);
    }

    public static int AskToClose(int processId)
    {
        var posted = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == (uint)processId && IsWindowVisible(window) && PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero))
            {
                posted++;
            }

            return true;
        }, IntPtr.Zero);
        return posted;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
