namespace IpGuard;

public static class SoundCatalog
{
    public const int Variants = 5;

    public static readonly (string Key, string Title, string[] Names)[] Kinds =
    {
        ("ready", "Готов к работе", new[] { "Два колокольчика вверх", "Три мягких колокольчика", "Маримба вверх", "Быстрое арпеджио", "Тёплый аккорд" }),
        ("changed", "IP отличается от эталона", new[] { "Три тона вниз", "Сирена", "Серии бипов", "Тревожные колокольчики", "Восходящая тревога" }),
        ("vpnoff", "VPN отключился", new[] { "Два электронных тона вниз", "Два колокольчика вниз", "Маримба вниз", "Выключение", "Два бипа вниз" }),
        ("noref", "Эталон не задан", new[] { "Маримба с эхом", "Колокольчик", "Двойной пинг", "Стеклянный «дзынь»", "Маримба вверх" }),
        ("offline", "Нет связи", new[] { "Низкий приглушённый тон", "Низкая маримба", "Глухой колокол", "Короткий спад", "Глухой удар" })
    };

    public static string? KeyFor(Status status) => status switch
    {
        Status.Unchanged => "ready",
        Status.Changed or Status.Leak => "changed",
        Status.VpnOff => "vpnoff",
        Status.NoReference => "noref",
        Status.Unavailable => "offline",
        _ => null
    };
}
