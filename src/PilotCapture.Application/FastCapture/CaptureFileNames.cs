using System.Globalization;
using System.Text.RegularExpressions;

namespace PilotCapture.Application.FastCapture;

public static class CaptureFileNames
{
    public static string Part(string? value) =>
        Regex.Replace(Regex.Replace(value?.Trim() ?? "", "[\\s<>:\"/\\\\|?*\\x00-\\x1f]+", "_"), "_+", "_").Trim('_', '.');

    public static string Station(string name)
    {
        var match = Regex.Match(name, @"(\d+)$");
        return match.Success ? match.Value : Part(name);
    }

    public static string Serial(long number)
    {
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        if (number <= 99) return number.ToString("00", CultureInfo.InvariantCulture);
        // No wrap: A0..AZ, B0..ZZ, then A00... as the local sequence grows.
        number -= 100;
        var width = 1;
        long block = 26 * 36;
        while (number >= block) { number -= block; block = checked(block * 36); width++; }
        long power = block / 26;
        var prefix = (char)('A' + number / power);
        number %= power;
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var tail = new char[width];
        for (var i = width - 1; i >= 0; i--) { tail[i] = digits[(int)(number % 36)]; number /= 36; }
        return prefix + new string(tail);
    }

    public static string Slot(int slot) => slot switch
    {
        -1 => "HC01",
        >= -14 and <= -11 => $"AC{-slot - 10:00}",
        >= -24 and <= -21 => $"M{-slot - 20:00}",
        >= -34 and <= -31 => $"T{-slot - 30:00}",
        > 0 => $"P{slot:00}",
        _ => ""
    };
    public static string SlotLabel(int slot) => slot switch
    {
        -1 => "Head Coach",
        >= -14 and <= -11 => $"Assistant Coach {-slot - 10}",
        >= -24 and <= -21 => $"Manager {-slot - 20}",
        >= -34 and <= -31 => $"Trainer {-slot - 30}",
        > 0 => $"Player {slot}",
        _ => ""
    };
    public static bool ValidSlot(int slot) => slot is >= 1 and <= 10000 or -1 or >= -14 and <= -11 or >= -24 and <= -21 or >= -34 and <= -31;
}

public sealed record CaptureColor(string Name, string Hex)
{
    public static IReadOnlyList<CaptureColor> Defaults { get; } =
    [
        new("None", "#FFFFFF"), new("Red", "#ff0000"), new("Blue", "#0606ff"),
        new("Lime Green", "#24ff00"), new("Orange", "#ff6701"), new("Pink", "#ff03ea"),
        new("Gold", "#ffb642"), new("Yellow", "#fcff00"), new("Auqa", "#00fff6"),
        new("Purple", "#5340a4"), new("Dark Green", "#1c6a0a"), new("Dark Gray", "#515151"),
        new("Gray", "#9e9e9e"), new("White", "#ffffff"), new("Light Green", "#0dcd90"), new("Maroon", "#800000")
    ];
    public static bool IsValid(CaptureColor color) => color.Name.Length is > 0 and <= 30 &&
        color.Name.All(c => char.IsLetter(c) || c == ' ') &&
        Regex.IsMatch(color.Hex, "^#[0-9a-fA-F]{6}$");
    public string FileSuffix => Name == "None" ? "" : "_" + Name.Replace(" ", "");
}
