namespace IpGuard;

public sealed record FooterButton(string Text, bool Primary, Action Click, Func<bool>? Enabled = null)
{
    public bool IsEnabled => Enabled?.Invoke() ?? true;
}

public abstract class Page
{
    protected Page(string title)
    {
        Title = title;
    }

    public string Title { get; protected set; }

    public RowList Rows { get; } = new();

    public IReadOnlyList<FooterButton> Footer { get; protected set; } = Array.Empty<FooterButton>();

    public PageHost? Host { get; internal set; }

    public virtual void OnShown()
    {
    }

    public virtual void OnInput()
    {
    }

    public virtual void OnTick(double now)
    {
    }

    public virtual bool OnBack() => true;
}

public sealed class PageHost
{
    private const float OpenSeconds = 0.35f;
    private const float EntrySeconds = 0.25f;
    private const float MaxStep = 0.5f;

    private readonly List<Page> stack = new();
    private float open;
    private float entry = 1f;
    private int footerHover = -1;
    private int footerPressed = -1;
    private bool backHover;
    private bool backPressed;
    private bool closeHover;
    private bool closePressed;

    public PageHost(PageFrame? frame = null)
    {
        Frame = frame ?? PageFrame.Panel;
    }

    public PageFrame Frame { get; }

    public bool BackClosesRoot { get; init; } = true;

    public bool ShowClose { get; init; }

    public bool CanGoBack => stack.Count > 1 || BackClosesRoot;

    public event Action? CloseRequested;

    public void RequestClose() => CloseRequested?.Invoke();

    public void Tick(double now) => Current?.OnTick(now);

    public bool CloseHover
    {
        get => closeHover;
        set
        {
            if (value != closeHover)
            {
                closeHover = value;
                Version++;
            }
        }
    }

    public bool ClosePressed
    {
        get => closePressed;
        set
        {
            if (value != closePressed)
            {
                closePressed = value;
                Version++;
            }
        }
    }

    public IReadOnlyList<Page> Stack => stack;

    public Page? Current => stack.Count == 0 ? null : stack[^1];

    public bool IsOpen => stack.Count > 0;

    public float Progress => EaseOut(open);

    public float ContentAlpha => EaseOut(entry);

    public int Version { get; private set; }

    public void Touch() => Version++;

    public event Action? Navigated;

    public int FooterHover
    {
        get => footerHover;
        set
        {
            if (value != footerHover)
            {
                footerHover = value;
                Version++;
            }
        }
    }

    public int FooterPressed
    {
        get => footerPressed;
        set
        {
            if (value != footerPressed)
            {
                footerPressed = value;
                Version++;
            }
        }
    }

    public bool BackHover
    {
        get => backHover;
        set
        {
            if (value != backHover)
            {
                backHover = value;
                Version++;
            }
        }
    }

    public bool BackPressed
    {
        get => backPressed;
        set
        {
            if (value != backPressed)
            {
                backPressed = value;
                Version++;
            }
        }
    }

    public void Push(Page page)
    {
        page.Host = this;
        Frame.Layout(page);
        stack.Add(page);
        entry = 0f;
        ResetFooter();
        page.OnShown();
        Version++;
        Navigated?.Invoke();
    }

    public bool Pop()
    {
        var page = Current;
        if (page is null || !CanGoBack || !page.OnBack())
        {
            return false;
        }

        stack.RemoveAt(stack.Count - 1);
        page.Host = null;
        entry = 0f;
        ResetFooter();
        if (Current is { } revealed)
        {
            Frame.Layout(revealed);
            revealed.OnShown();
        }

        Version++;
        Navigated?.Invoke();
        return true;
    }

    public void Clear()
    {
        foreach (var page in stack)
        {
            page.Host = null;
        }

        stack.Clear();
        ResetFooter();
        Version++;
    }

    public void Step(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0)
        {
            return;
        }

        var h = Math.Min(dt, MaxStep);
        var target = IsOpen ? 1f : 0f;
        var next = open < target ? Math.Min(target, open + h / OpenSeconds) : Math.Max(target, open - h / (OpenSeconds * 0.6f));
        var nextEntry = Math.Min(1f, entry + h / EntrySeconds);
        if (next != open || nextEntry != entry)
        {
            open = next;
            entry = nextEntry;
            Version++;
        }
    }

    public void Accept()
    {
        var footer = Current?.Footer;
        if (footer is null)
        {
            return;
        }

        for (var i = footer.Count - 1; i >= 0; i--)
        {
            if (footer[i].Primary)
            {
                ClickFooter(i);
                return;
            }
        }
    }

    public void ClickFooter(int index)
    {
        var footer = Current?.Footer;
        if (footer is null || index < 0 || index >= footer.Count)
        {
            return;
        }

        var button = footer[index];
        if (button.IsEnabled)
        {
            button.Click();
        }
    }

    private void ResetFooter()
    {
        footerHover = -1;
        footerPressed = -1;
        backHover = false;
        backPressed = false;
        closeHover = false;
        closePressed = false;
    }

    private static float EaseOut(float t)
    {
        var x = Math.Clamp(t, 0f, 1f);
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }
}
