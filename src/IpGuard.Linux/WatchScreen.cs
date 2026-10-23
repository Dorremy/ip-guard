using System.Text;

namespace IpGuard;

public static class WatchScreen
{
    public const string WindowsClientNote = "Изменения — в окне IpGuard";

    private const string Reset = "\u001b[0m";
    private const string Bold = "\u001b[1m";
    private const string Dim = "\u001b[2m";
    private const string Green = "\u001b[32m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const int MaxJournalRows = 5;

    public static IReadOnlyList<string> Render(WatchModel model, int width, int height, bool color)
    {
        var limit = Math.Max(1, width - 1);
        var paint = new Painter(color, limit);
        var top = new List<string>
        {
            paint.Line(Header(model), Dim),
            ""
        };
        top.AddRange(Summary(model, paint));

        var bottom = new List<string>();
        if (model.Interactive)
        {
            bottom.Add(paint.Line(model.Prompt ?? model.Notice ?? "", model.Prompt is null ? "" : Bold));
            bottom.Add(paint.Line(Hints(model), Dim));
        }

        var room = Math.Max(0, height - top.Count - bottom.Count);
        var body = new List<string>(top);
        body.AddRange(Sections(model, paint, room));
        if (body.Count > height - bottom.Count)
        {
            body = body.Take(Math.Max(0, height - bottom.Count)).ToList();
        }

        if (!model.Interactive)
        {
            return body;
        }

        while (body.Count < height - bottom.Count)
        {
            body.Add("");
        }

        body.AddRange(bottom);
        return body.Take(height).ToList();
    }

    public static WatchCaps Caps(WatchModel model) => WatchCaps.For(model.Mode, model.Windows);

    private static string Header(WatchModel model) => model.Mode switch
    {
        WatchMode.Own => "IpGuard · мониторинг в этом окне",
        WatchMode.Client => "IpGuard · служба работает",
        _ => "IpGuard · мониторинг не запущен"
    };

    private static IEnumerable<string> Summary(WatchModel model, Painter paint)
    {
        if (model.Starting)
        {
            yield return paint.Line("Идёт первая проверка…", Yellow + Bold);
            yield break;
        }

        if (model.File == StatusFile.Missing)
        {
            yield return paint.Line("Служба ещё не запускалась", Yellow + Bold);
            if (model.Mode != WatchMode.Own)
            {
                yield return paint.Line("Запустите: ipguard run", "");
            }

            yield break;
        }

        if (model.File == StatusFile.Unreadable || model.Report is not { } report)
        {
            yield return paint.Line("Файл статуса не читается", Yellow + Bold);
            yield break;
        }

        yield return paint.Line(report.Title, TitleColor(model, report) + Bold);
        if (report.Message.Length > 0)
        {
            yield return paint.Line(report.Message, "");
        }

        yield return paint.Line(StatusCommand.Place(report), "");
        if (report.References.Count > 0)
        {
            yield return paint.Line("Эталоны: " + string.Join(", ", report.References), "");
        }

        if (report.Leak.Length > 0)
        {
            yield return paint.Line("Утечка: " + report.Leak, Red);
        }

        yield return paint.Line($"Обновлено {StatusCommand.Ago(model.Age)}", Dim);
        if (model.Stale)
        {
            yield return paint.Line("Статус устарел: служба не отвечает", Yellow);
        }
    }

    private static string TitleColor(WatchModel model, StatusReport report)
    {
        if (model.Stale)
        {
            return Yellow;
        }

        return report.State switch
        {
            "ok" => Green,
            "stopped" or "no_reference" or "home" or "offline" => Yellow,
            _ => Red
        };
    }

    private static IEnumerable<string> Sections(WatchModel model, Painter paint, int room)
    {
        var lines = new List<string>();
        var journalRows = 0;
        if (model.Journal.Count > 0 && room >= 10)
        {
            journalRows = Math.Min(Math.Min(model.Journal.Count, MaxJournalRows), room / 4);
        }

        var journalCost = journalRows > 0 ? journalRows + 2 : 0;
        var historyRows = Math.Min(model.History.Count, room - journalCost - 2);
        if (historyRows > 0)
        {
            lines.Add("");
            lines.Add(paint.Line("История", Bold));
            lines.AddRange(model.History.Take(historyRows).Select(entry => paint.Line(HistoryLine(entry), "")));
        }

        if (journalRows > 0)
        {
            lines.Add("");
            lines.Add(paint.Line("Журнал", Bold));
            lines.AddRange(model.Journal.Skip(model.Journal.Count - journalRows).Select(line => paint.Line(line, Dim)));
        }

        return lines;
    }

    private static string HistoryLine(HistoryEntry entry)
    {
        var text = $"{entry.Time:MM-dd HH:mm}  {entry.Ip,-15}  {entry.CountryCode,-2}  {Labels.For(entry.Status)}";
        return string.IsNullOrWhiteSpace(entry.Note) ? text : $"{text} · {entry.Note}";
    }

    private static string Hints(WatchModel model)
    {
        var caps = Caps(model);
        var parts = new List<string> { "q выход" };
        if (caps.CanWrite)
        {
            parts.Add("t запомнить IP");
            parts.Add("h домашняя страна");
        }

        if (caps.CanCheck)
        {
            parts.Add("c проверить сейчас");
        }

        var text = string.Join("  ", parts);
        return caps.GuiOwnsWrites ? $"{text}  ·  {WindowsClientNote}" : text;
    }

    private sealed class Painter
    {
        private readonly bool color;
        private readonly int limit;

        public Painter(bool color, int limit)
        {
            this.color = color;
            this.limit = limit;
        }

        public string Line(string text, string style)
        {
            var fitted = Fit(Clean(text), limit);
            return color && style.Length > 0 ? style + fitted + Reset : fitted;
        }
    }

    public static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    public static string Fit(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        var cut = Math.Max(0, limit - 1);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }
}
