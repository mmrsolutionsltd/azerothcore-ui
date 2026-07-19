namespace AzerothCore_UI.Api.Data;

public static class AreaNameResolver
{
    private static readonly IReadOnlyDictionary<ushort, string> CanonicalNames =
        new Dictionary<ushort, string>
        {
            [1638] = "Thunder Bluff"
        };

    public static string Resolve(ushort zoneId, string? databaseName)
    {
        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            return databaseName;
        }

        return CanonicalNames.TryGetValue(zoneId, out var canonicalName)
            ? canonicalName
            : $"Zone {zoneId}";
    }
}
