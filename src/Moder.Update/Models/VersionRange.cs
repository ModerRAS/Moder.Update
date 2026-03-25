namespace Moder.Update.Models;

/// <summary>
/// Utility for version range checks and update path computation.
/// </summary>
public static class VersionRange
{
    /// <summary>
    /// Checks whether <paramref name="version"/> is within [<paramref name="min"/>, <paramref name="max"/>].
    /// If <paramref name="max"/> is null, only the lower bound is checked.
    /// </summary>
    public static bool Contains(Version version, Version min, Version? max)
    {
        if (version < min)
            return false;
        if (max is not null && version > max)
            return false;
        return true;
    }

    /// <summary>
    /// Determines whether there is a valid update path from <paramref name="current"/> to <paramref name="target"/>
    /// through the provided manifests.
    /// </summary>
    public static bool CanReach(Version current, Version target, IEnumerable<UpdateManifest> chain)
    {
        return GetUpdatePath(current, target, chain).Any();
    }

    /// <summary>
    /// Computes the ordered sequence of manifests to apply to go from <paramref name="from"/> to <paramref name="to"/>.
    /// Prefers cumulative packages when available.
    /// </summary>
    public static IReadOnlyList<UpdateManifest> GetUpdatePath(
        Version from, Version to, IEnumerable<UpdateManifest> chain)
    {
        var manifests = chain.ToList();
        var path = new List<UpdateManifest>();
        var current = from;

        while (current < to)
        {
            var cumulative = manifests
                .Where(m => m.IsCumulative
                    && Version.TryParse(m.TargetVersion, out var tv)
                    && tv <= to
                    && Version.TryParse(m.MinSourceVersion, out var minV)
                    && Contains(current, minV,
                        m.MaxSourceVersion is not null && Version.TryParse(m.MaxSourceVersion, out var maxV)
                            ? maxV : null))
                .OrderByDescending(m => Version.Parse(m.TargetVersion))
                .FirstOrDefault();

            if (cumulative is not null)
            {
                path.Add(cumulative);
                current = Version.Parse(cumulative.TargetVersion);
                continue;
            }

            var next = manifests
                .Where(m =>
                    Version.TryParse(m.TargetVersion, out var tv)
                    && tv <= to
                    && Version.TryParse(m.MinSourceVersion, out var minV)
                    && Contains(current, minV,
                        m.MaxSourceVersion is not null && Version.TryParse(m.MaxSourceVersion, out var maxV)
                            ? maxV : null))
                .OrderBy(m => Version.Parse(m.TargetVersion))
                .FirstOrDefault();

            if (next is null)
                break;

            path.Add(next);
            current = Version.Parse(next.TargetVersion);
        }

        if (current < to)
            return [];

        return path;
    }

    /// <summary>
    /// Computes the update path using catalog entries instead of manifests.
    /// </summary>
    public static IReadOnlyList<UpdateCatalogEntry> GetUpdatePath(
        Version from, Version to, IEnumerable<UpdateCatalogEntry> entries)
    {
        var entryList = entries.ToList();
        var path = new List<UpdateCatalogEntry>();
        var current = from;

        while (current < to)
        {
            var cumulative = entryList
                .Where(e => e.IsCumulative
                    && Version.TryParse(e.TargetVersion, out var tv)
                    && tv <= to
                    && Version.TryParse(e.MinSourceVersion, out var minV)
                    && Contains(current, minV,
                        e.MaxSourceVersion is not null && Version.TryParse(e.MaxSourceVersion, out var maxV)
                            ? maxV : null))
                .OrderByDescending(e => Version.Parse(e.TargetVersion))
                .FirstOrDefault();

            if (cumulative is not null)
            {
                path.Add(cumulative);
                current = Version.Parse(cumulative.TargetVersion);
                continue;
            }

            var next = entryList
                .Where(e =>
                    Version.TryParse(e.TargetVersion, out var tv)
                    && tv <= to
                    && Version.TryParse(e.MinSourceVersion, out var minV)
                    && Contains(current, minV,
                        e.MaxSourceVersion is not null && Version.TryParse(e.MaxSourceVersion, out var maxV)
                            ? maxV : null))
                .OrderBy(e => Version.Parse(e.TargetVersion))
                .FirstOrDefault();

            if (next is null)
                break;

            path.Add(next);
            current = Version.Parse(next.TargetVersion);
        }

        if (current < to)
            return [];

        return path;
    }
}
