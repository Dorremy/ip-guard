namespace IpGuard;

public static class ErrorText
{
    private const int MaxLength = 400;

    public static string Format(string where, Exception? error)
    {
        if (error is null)
        {
            return $"Ошибка ({where}): причина неизвестна";
        }

        var root = error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0 ? aggregate.InnerExceptions[0] : error;
        var frame = (root.StackTrace ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        var text = $"Ошибка ({where}): {root.GetType().Name}: {root.Message}";
        if (frame.Length > 0)
        {
            text += $" | {frame}";
        }

        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length > MaxLength ? text[..MaxLength] + "…" : text;
    }
}
