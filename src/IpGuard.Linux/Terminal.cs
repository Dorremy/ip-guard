using System.Runtime.InteropServices;
using System.Text;

namespace IpGuard;

public sealed class Terminal : IDisposable
{
    private const string Enter = "\u001b[?1049h\u001b[?25l";
    private const string Leave = "\u001b[?25h\u001b[?1049l";
    private const int StdOutput = -11;
    private const uint VirtualTerminalProcessing = 0x0004;

    private readonly bool vt;
    private readonly bool controlAsInput;
    private bool active;

    private Terminal(bool vt, bool controlAsInput)
    {
        this.vt = vt;
        this.controlAsInput = controlAsInput;
    }

    public bool Color => vt;

    public static Terminal Open()
    {
        var vt = !OperatingSystem.IsWindows() || EnableVirtualTerminal();
        var previous = SafeControlAsInput();
        var terminal = new Terminal(vt, previous);
        terminal.Start();
        return terminal;
    }

    public (int Width, int Height) Size()
    {
        try
        {
            return (Math.Max(1, Console.WindowWidth), Math.Max(1, Console.WindowHeight));
        }
        catch (IOException)
        {
            return (80, 24);
        }
    }

    public void Draw(IReadOnlyList<string> lines, int width, int height)
    {
        var builder = new StringBuilder();
        if (vt)
        {
            builder.Append("\u001b[H");
            for (var i = 0; i < height; i++)
            {
                builder.Append(i < lines.Count ? lines[i] : "").Append("\u001b[K");
                if (i < height - 1)
                {
                    builder.Append("\r\n");
                }
            }

            builder.Append("\u001b[J");
        }
        else
        {
            Console.SetCursorPosition(0, 0);
            for (var i = 0; i < height; i++)
            {
                builder.Append((i < lines.Count ? lines[i] : "").PadRight(Math.Max(0, width - 1)));
                if (i < height - 1)
                {
                    builder.Append("\r\n");
                }
            }
        }

        Console.Out.Write(builder.ToString());
        Console.Out.Flush();
    }

    public void Dispose()
    {
        if (!active)
        {
            return;
        }

        active = false;
        try
        {
            if (vt)
            {
                Console.Out.Write(Leave);
            }
            else
            {
                Console.Clear();
                Console.CursorVisible = true;
            }

            Console.Out.Flush();
            Console.TreatControlCAsInput = controlAsInput;
        }
        catch (IOException)
        {
        }
    }

    private void Start()
    {
        active = true;
        Console.TreatControlCAsInput = true;
        if (vt)
        {
            Console.Out.Write(Enter);
        }
        else
        {
            Console.Clear();
            Console.CursorVisible = false;
        }

        Console.Out.Flush();
    }

    private static bool SafeControlAsInput()
    {
        try
        {
            return Console.TreatControlCAsInput;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var handle = GetStdHandle(StdOutput);
            return GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | VirtualTerminalProcessing);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
