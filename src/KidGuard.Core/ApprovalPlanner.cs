namespace KidGuard.Core;

public record ApprovalPlan(HashSet<Guid> Playable, HashSet<Guid> Navigation);
public static class ApprovalPlanner
{
    public static ApprovalPlan Build(ChildProfile profile, IReadOnlyDictionary<Guid, Assessment> cache)
    {
        var playable = new HashSet<Guid>();
        foreach (var assessment in cache.Values.Where(a => a.Item.Kind is MediaKind.Movie or MediaKind.Episode))
        {
            var item = assessment.Item;
            var decision = profile.Recommendations.GetValueOrDefault(item.Id)?.Decision ?? Decision.Review;
            // Explicit per-item choice beats container choices. Closest container override wins.
            if (profile.Overrides.GetValueOrDefault(item.Id) is not OverrideKind.None)
                decision = ToDecision(profile.Overrides[item.Id]);
            else
            {
                foreach (var ancestor in item.Ancestors)
                {
                    var inherited = profile.Overrides.GetValueOrDefault(ancestor);
                    if (inherited == OverrideKind.None) continue;
                    decision = ToDecision(inherited); break;
                }
            }
            if (decision == Decision.Allow) playable.Add(item.Id);
        }
        var navigation = playable.SelectMany(id => cache[id].Item.Ancestors).ToHashSet();
        return new(playable, navigation);
    }
    public static Decision ToDecision(OverrideKind value) => value is OverrideKind.Allow or OverrideKind.AlwaysAllow ? Decision.Allow : Decision.Block;
    public static string Tag(Guid profileId, Guid generation) => $"KidGuard:{profileId:N}:{generation:N}";
    public static string[] UpdateTags(IEnumerable<string> tags, string? remove, string? add)
        => tags.Where(t => !string.Equals(t, remove, StringComparison.OrdinalIgnoreCase)).Concat(add is null ? [] : new[] { add }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
