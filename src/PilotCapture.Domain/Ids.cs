namespace PilotCapture.Domain;

public static class Ids
{
    public static string New() => Ulid.NewUlid().ToString().ToLowerInvariant();
}

public static class StationCodes
{
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        "s10", "s20", "s30", "s40"
    };

    public static bool IsSupported(string code) => Supported.Contains(code);
}
