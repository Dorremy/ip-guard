namespace IpGuard;

public static class Countries
{
    public static readonly (string Code, string Name)[] All =
    {
        ("RU", "Россия"), ("BY", "Беларусь"), ("UA", "Украина"), ("KZ", "Казахстан"), ("UZ", "Узбекистан"),
        ("KG", "Киргизия"), ("TJ", "Таджикистан"), ("TM", "Туркменистан"), ("AM", "Армения"), ("AZ", "Азербайджан"),
        ("GE", "Грузия"), ("MD", "Молдова"), ("LV", "Латвия"), ("LT", "Литва"), ("EE", "Эстония"),
        ("PL", "Польша"), ("CZ", "Чехия"), ("SK", "Словакия"), ("HU", "Венгрия"), ("RO", "Румыния"),
        ("BG", "Болгария"), ("RS", "Сербия"), ("HR", "Хорватия"), ("SI", "Словения"), ("GR", "Греция"),
        ("CY", "Кипр"), ("TR", "Турция"), ("DE", "Германия"), ("AT", "Австрия"), ("CH", "Швейцария"),
        ("NL", "Нидерланды"), ("BE", "Бельгия"), ("LU", "Люксембург"), ("FR", "Франция"), ("GB", "Великобритания"),
        ("IE", "Ирландия"), ("ES", "Испания"), ("PT", "Португалия"), ("IT", "Италия"), ("FI", "Финляндия"),
        ("SE", "Швеция"), ("NO", "Норвегия"), ("DK", "Дания"), ("IS", "Исландия"), ("US", "США"),
        ("CA", "Канада"), ("MX", "Мексика"), ("BR", "Бразилия"), ("AR", "Аргентина"), ("CL", "Чили"),
        ("AE", "ОАЭ"), ("IL", "Израиль"), ("IN", "Индия"), ("CN", "Китай"), ("HK", "Гонконг"),
        ("JP", "Япония"), ("KR", "Южная Корея"), ("SG", "Сингапур"), ("TH", "Таиланд"), ("VN", "Вьетнам"),
        ("ID", "Индонезия"), ("MY", "Малайзия"), ("AU", "Австралия"), ("NZ", "Новая Зеландия"), ("ZA", "ЮАР")
    };

    public static string Describe(string code)
    {
        var match = All.FirstOrDefault(country => country.Code == code);
        return match.Code is null ? code : $"{match.Code} — {match.Name}";
    }

    public static IReadOnlyList<(string Code, string Title)> Choices(string current)
    {
        var list = All.Select(country => (country.Code, Describe(country.Code))).ToList();
        if (!string.IsNullOrWhiteSpace(current) && list.All(item => item.Code != current))
        {
            list.Insert(0, (current, current));
        }

        return list;
    }
}
