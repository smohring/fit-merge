using System.Reflection;
using Dynastream.Fit;

namespace FitMerge.Services;

/// <summary>Resolves device names to product IDs through the FIT SDK profile (<see cref="GarminProduct"/>), never from hardcoded numbers.</summary>
public static class GarminProducts
{
    private static readonly Dictionary<ushort, string> ById = ConstantsById(typeof(GarminProduct));
    private static readonly Dictionary<ushort, string> ManufacturersById = ConstantsById(typeof(Manufacturer));
    private static readonly Dictionary<string, ushort> ByName = ById.ToDictionary(p => Normalize(p.Value), p => p.Key);

    /// <summary>All product names of the SDK profile, alphabetically; feeds the device selection in the UI.</summary>
    public static IReadOnlyList<string> AllNames { get; } = ById.Values.OrderBy(n => n).ToArray();

    /// <summary>The profile classes hold ushort constants rather than enums, so they are read by reflection.</summary>
    private static Dictionary<ushort, string> ConstantsById(Type profile) => profile
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
        .GroupBy(f => (ushort)f.GetRawConstantValue()!)
        .ToDictionary(g => g.Key, g => g.First().Name);

    public static string? ManufacturerName(ushort manufacturer) => ManufacturersById.GetValueOrDefault(manufacturer);

    /// <summary>Name from the SDK profile, or <c>null</c> if the ID is not listed.</summary>
    public static string? NameOf(ushort product) => ById.GetValueOrDefault(product);

    /// <summary>
    /// Resolves "edge_1040", "Edge1040" or "3843" to a product ID. Unknown names throw
    /// <see cref="ArgumentException"/> listing similar candidates instead of guessing.
    /// </summary>
    public static ushort Resolve(string name)
    {
        var input = name.Trim();
        if (input.Length == 0)
            throw new ArgumentException(Loc.Get("ErrNoDeviceName"));

        if (ushort.TryParse(input, out var numeric))
            return numeric; // explicit ID for devices the SDK profile does not know yet
        if (ByName.TryGetValue(Normalize(input), out var product))
            return product;

        throw new ArgumentException(
            Loc.Get("ErrUnknownDevice", input, AllNames.Count) + SuggestionText(input) +
            "\n\n" + Loc.Get("SuggestNumeric"));
    }

    /// <summary>Up to 25 similar names to append to the error message.</summary>
    private static string SuggestionText(string input)
    {
        var needle = Normalize(input);
        var matches = AllNames
            .Where(n => Normalize(n).Contains(needle) || needle.Contains(Normalize(n)))
            .Take(25)
            .ToList();
        if (matches.Count == 0)
            matches = AllNames.Where(n => Normalize(n).StartsWith(needle[..1])).Take(25).ToList();

        return matches.Count == 0 ? "" : $"\n\n{Loc.Get("SuggestHeader")}\n  {string.Join("\n  ", matches)}";
    }

    private static string Normalize(string name) =>
        name.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
}
