namespace EngineeringEssentials.Demos;

/// <summary>Tiny console helpers so each demo reads like the slides.</summary>
internal static class Ui
{
    public static void Title(string text)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 64));
        Console.WriteLine("  " + text);
        Console.WriteLine(new string('=', 64));
    }

    public static void Section(string text)
    {
        Console.WriteLine();
        Console.WriteLine("── " + text + " " + new string('─', Math.Max(0, 58 - text.Length)));
    }

    /// <summary>Print an expression's result next to the source, slide-style.</summary>
    public static void Show(string expr, object? value)
        => Console.WriteLine($"  {expr,-46} => {Format(value)}");

    public static void Note(string text) => Console.WriteLine("  • " + text);

    private static string Format(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "null",
    };
}
