namespace IpGuard;

public sealed record WatchCaps(bool CanWrite, bool CanCheck, bool GuiOwnsWrites)
{
    public static WatchCaps For(WatchMode mode, bool windows) => mode switch
    {
        WatchMode.Own => new WatchCaps(true, true, false),
        WatchMode.Client when windows => new WatchCaps(false, false, true),
        WatchMode.Client => new WatchCaps(true, false, false),
        _ => new WatchCaps(false, false, false)
    };
}

public interface IWatchActions
{
    Task<string> TrustAsync();

    Task<string> SetHomeAsync(string code);

    Task CheckNowAsync();
}

public enum WatchKeyResult
{
    None,
    Redraw,
    Quit
}

public sealed class WatchController
{
    private static readonly TimeSpan NoticeLife = TimeSpan.FromSeconds(10);

    private readonly WatchCaps caps;
    private readonly IWatchActions actions;
    private readonly Func<DateTime> utcNow;
    private string? prompt;
    private string? notice;
    private DateTime noticeAt;

    public WatchController(WatchCaps caps, IWatchActions actions, Func<DateTime> utcNow)
    {
        this.caps = caps;
        this.actions = actions;
        this.utcNow = utcNow;
    }

    public Task Running { get; private set; } = Task.CompletedTask;

    public string? Prompt => prompt is null ? null : $"Домашняя страна (две буквы, Esc — отмена): {prompt}_";

    public string? Notice => notice is not null && utcNow() - noticeAt < NoticeLife ? notice : null;

    public WatchKeyResult Handle(ConsoleKeyInfo key)
    {
        if (prompt is not null)
        {
            return HandlePrompt(key);
        }

        if (key.Key == ConsoleKey.Escape || IsInterrupt(key))
        {
            return WatchKeyResult.Quit;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'q' or 'й':
                return WatchKeyResult.Quit;
            case 't' or 'е':
                return caps.CanWrite ? Start(actions.TrustAsync, "Определяю IP…") : Blocked();
            case 'h' or 'р':
                if (!caps.CanWrite)
                {
                    return Blocked();
                }

                prompt = "";
                notice = null;
                return WatchKeyResult.Redraw;
            case 'c' or 'с' when caps.CanCheck:
                return Start(async () =>
                {
                    await actions.CheckNowAsync();
                    return "Проверка выполнена";
                }, "Проверяю…");
            default:
                return WatchKeyResult.None;
        }
    }

    private static bool IsInterrupt(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control);

    private WatchKeyResult Blocked()
    {
        if (!caps.GuiOwnsWrites)
        {
            return WatchKeyResult.None;
        }

        SetNotice(WatchScreen.WindowsClientNote);
        return WatchKeyResult.Redraw;
    }

    private WatchKeyResult HandlePrompt(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || IsInterrupt(key))
        {
            prompt = null;
            return WatchKeyResult.Redraw;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            var code = prompt!;
            if (!HomeCommand.IsValid(code))
            {
                SetNotice("Код страны — ровно две латинские буквы, например: RU");
                return WatchKeyResult.Redraw;
            }

            prompt = null;
            return Start(() => actions.SetHomeAsync(code), "Сохраняю…");
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (prompt!.Length > 0)
            {
                prompt = prompt[..^1];
            }
        }
        else if (char.IsAsciiLetter(key.KeyChar) && prompt!.Length < 2)
        {
            prompt += char.ToUpperInvariant(key.KeyChar);
        }

        return WatchKeyResult.Redraw;
    }

    private WatchKeyResult Start(Func<Task<string>> action, string progress)
    {
        if (!Running.IsCompleted)
        {
            SetNotice("Предыдущее действие ещё выполняется");
            return WatchKeyResult.Redraw;
        }

        SetNotice(progress);
        Running = RunAsync(action);
        return WatchKeyResult.Redraw;
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        try
        {
            SetNotice(await action());
        }
        catch (Exception e)
        {
            SetNotice($"Ошибка: {e.Message}");
        }
    }

    private void SetNotice(string text)
    {
        notice = text;
        noticeAt = utcNow();
    }
}
