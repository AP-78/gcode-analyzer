namespace GcodeAnalyzer.Core;

/// <summary>Support-Einstellungen und tatsächlich erzeugte Support-Pfade (Support und Support-Interface).</summary>
public sealed record SupportAnalysis(bool Enabled, string? Type, bool? BuildPlateOnly, bool HasPaths,
    double FilamentPercent, double TimePercent)
{
    /// <summary>Support-Widerspruch: aktiviert, aber keine Support-Pfade in der Datei.</summary>
    public bool Contradiction => Enabled && !HasPaths;

    internal static SupportAnalysis From(IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<PathCategory, CategoryShare> shares)
    {
        string? Get(params string[] keys) => keys.Select(settings.GetValueOrDefault).FirstOrDefault(v => v != null);
        bool? Flag(params string[] keys) => Get(keys) is { } v ? v.Trim() is "1" or "true" : null;
        var parts = shares.Where(kv => kv.Key is PathCategory.Support or PathCategory.SupportInterface).Select(kv => kv.Value).ToList();
        return new SupportAnalysis(
            Flag("enable_support", "support_material") ?? false,
            Get("support_type", "support_material_style"),
            Flag("support_on_build_plate_only", "support_material_buildplate_only"),
            parts.Any(p => p.FilamentMm > 0),
            parts.Sum(p => p.FilamentPercent), parts.Sum(p => p.TimePercent));
    }
}
