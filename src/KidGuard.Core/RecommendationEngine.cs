namespace KidGuard.Core;

/// <summary>Transparent editorial heuristics, not measured probabilities or a developmental diagnosis.</summary>
public static class Ratings
{
    // These are conservative editorial starting points, not official age minima for PG/G.
    private static readonly Dictionary<string, int> Ages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G"] = 0, ["TV-Y"] = 0, ["TV-G"] = 0, ["TV-Y7"] = 7, ["TV-Y7-FV"] = 7,
        ["PG"] = 8, ["TV-PG"] = 12, ["PG-13"] = 13, ["TV-14"] = 14, ["R"] = 17,
        ["NC-17"] = 18, ["TV-MA"] = 18, ["GB-U"] = 0, ["GB-PG"] = 8,
        ["GB-12"] = 12, ["GB-12A"] = 12, ["GB-15"] = 15, ["GB-18"] = 18,
        ["DE-0"] = 0, ["DE-6"] = 6, ["DE-12"] = 12, ["DE-16"] = 16, ["DE-18"] = 18
    };
    public static int? Age(string? rating) => rating is not null && Ages.TryGetValue(rating.Trim(), out var age) ? age : null;
}
public static class CalibrationEngine
{
    public static readonly LibraryItem[] References = Enumerable.Range(0, 6).Select(i => new LibraryItem(
        Guid.Parse($"00000000-0000-4000-8000-{i+1:000000000000}"),
        new[] { "Reference: gentle preschool story", "Reference: mild cartoon adventure", "Reference: family fantasy adventure", "Reference: teen action story", "Reference: intense teen drama", "Reference: adult drama" }[i],
        MediaKind.Movie, null, new[] { "G", "TV-Y7", "PG", "PG-13", "TV-14", "TV-MA" }[i], null, [], [],
        "Hypothetical calibration scenario, not an actual title or a scene-level advisory.", new() { ["KidGuardReference"] = "true" }, [])).ToArray();
    public static int Tolerance(ChildProfile profile, Dimension dimension, IReadOnlyDictionary<Guid, Assessment> cache)
    {
        if (profile.Tolerances.TryGetValue(dimension, out var explicitValue)) return explicitValue;
        var baseline = profile.Age < 8 ? 1 : profile.Age < 13 ? 2 : 3;
        var yes = new List<int>(); var no = new List<int>();
        foreach (var answer in profile.Calibration)
        {
            if (!cache.TryGetValue(answer.ItemId, out var a)) continue;
            var levels = a.Evidence.Where(e => !e.Failed && e.Dimensions.ContainsKey(dimension)).Select(e => e.Dimensions[dimension]).ToArray();
            if (levels.Length == 0) continue;
            if (answer.Answer == Decision.Allow) yes.Add(levels.Max());
            if (answer.Answer == Decision.Block) no.Add(levels.Max());
        }
        var inferred = yes.Count > 0 ? yes.Max() : baseline;
        if (no.Count > 0) inferred = Math.Min(inferred, Math.Max(0, no.Min() - 1));
        return Math.Clamp(inferred, Math.Max(0, baseline - 1), Math.Min(4, baseline + 1));
    }

    public static double Offset(ChildProfile profile, IReadOnlyDictionary<Guid, Assessment> cache)
    {
        var yes = new List<int>(); var no = new List<int>();
        foreach (var answer in profile.Calibration)
        {
            int age;
            if (cache.TryGetValue(answer.ItemId, out var assessment))
                age = assessment.Evidence.Select(e => Ratings.Age(e.Rating)).Where(a => a.HasValue).Select(a => a!.Value).DefaultIfEmpty(-1).Max();
            else age = Ratings.Age(References.FirstOrDefault(r => r.Id == answer.ItemId)?.Rating) ?? -1;
            if (age < 0) continue;
            if (answer.Answer == Decision.Allow) yes.Add(age);
            if (answer.Answer == Decision.Block) no.Add(age);
        }
        if (yes.Count == 0 && no.Count == 0) return 0;
        var low = yes.Count > 0 ? yes.Max() : profile.Age;
        var high = no.Count > 0 ? no.Min() - 1 : low;
        // Contradictory answers never increase the ceiling.
        if (high < low) return Math.Clamp(high - profile.Age, -3, 0);
        return Math.Clamp((low + high) / 2.0 - profile.Age, -3, 3);
    }
    public static LibraryItem[] SelectExamples(IEnumerable<Assessment> assessments)
    {
        var groups = assessments.Where(a => a.Item.Kind is MediaKind.Movie or MediaKind.Series)
            .Select(a => (a.Item, Age: Ratings.Age(a.Item.Rating))).Where(a => a.Age.HasValue)
            .GroupBy(a => a.Age!.Value).OrderBy(g => g.Key).ToArray();
        var selected = groups.Select(g => g.OrderBy(x => x.Item.Title, StringComparer.Ordinal).First().Item).Take(10).ToList();
        selected.AddRange(groups.SelectMany(g => g).Select(x => x.Item).Where(i => selected.All(s => s.Id != i.Id)).Take(10 - selected.Count));
        if (selected.Count < 6) selected.AddRange(References.Take(6 - selected.Count));
        return selected.ToArray();
    }
}
public sealed class RecommendationEngine
{
    public Recommendation Evaluate(ChildProfile profile, Assessment assessment, IReadOnlyDictionary<Guid, Assessment> cache)
    {
        var evidence = new List<Advisory>(assessment.Evidence);
        // Only absent episode/season certification falls back to the closest rated TV ancestor.
        // Keep provenance explicit and never pretend series advisories describe an individual episode.
        if (assessment.Item.Kind is MediaKind.Episode or MediaKind.Season &&
            !evidence.Any(e => !e.Failed && Ratings.Age(e.Rating).HasValue))
        {
            foreach (var id in assessment.Item.Ancestors)
            {
                if (!cache.TryGetValue(id, out var parent) || parent.Item.Kind is not (MediaKind.Series or MediaKind.Season)) continue;
                if (!parent.Evidence.Any(e => !e.Failed && Ratings.Age(e.Rating).HasValue)) continue;
                evidence.AddRange(parent.Evidence.Where(e => e.Failed || Ratings.Age(e.Rating).HasValue)
                    .Select(e => new Advisory($"{e.Source} (inherited from {parent.Item.Title})", e.Rating, [],
                        $"Using {parent.Item.Kind.ToString().ToLowerInvariant()} certification from '{parent.Item.Title}' because this item has no recognized certification. This is not episode-specific evidence.", e.Failed)));
                break;
            }
        }
        var sources = evidence.Select(e => e.Source).Distinct().ToArray();
        var ageValues = evidence.Where(e => !e.Failed).Select(e => Ratings.Age(e.Rating)).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        int? age = ageValues.Length == 0 ? null : ageValues.Max();
        if (profile.Overrides.TryGetValue(assessment.Item.Id, out var manual) && manual != OverrideKind.None)
            return new(manual is OverrideKind.Allow or OverrideKind.AlwaysAllow ? Decision.Allow : Decision.Block,
                100, age, [$"Explicit parent decision: {manual}. Content certainty is unchanged."], sources);
        var reasons = new List<string>();
        var offset = CalibrationEngine.Offset(profile, cache);
        var ceiling = Math.Clamp(profile.Age + profile.MaturityOffset + offset + (profile.Approach == Approach.Conservative ? -1 : profile.Approach == Approach.Permissive ? 1 : 0), 0, 18);
        reasons.Add($"Profile comfort ceiling {ceiling:0.#}; calibration adjustment {offset:+0.#;-0.#;0}.");
        var childrensPass = profile.Age >= 9 && assessment.Item.Kind is (MediaKind.Series or MediaKind.Season or MediaKind.Episode)
            && evidence.Any(e => !e.Failed && e.Rating?.Trim().ToUpperInvariant() is ("TV-Y" or "TV-Y7" or "TV-Y7-FV"))
            && ageValues.All(a => a <= 7);
        if (childrensPass)
        {
            ceiling = Math.Max(ceiling, 7);
            reasons.Add("Age 9+ children's-TV rule: TV-Y/TV-Y7 recommendations do not require extra review solely because calibration is gentler. Explicit content limits still apply.");
        }
        reasons.AddRange(evidence.Skip(assessment.Evidence.Count).Select(e => e.Note).Distinct());
        if (age.HasValue)
            reasons.Add($"Reported certification: {string.Join(", ", evidence.Where(e => !e.Failed).Select(e => e.Rating).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())}; editorial age starting point {age}.");
        // Uncertainty cannot turn an established above-ceiling rating into an ambiguous Review.
        if (age > ceiling)
            return new(Decision.Block, 90, age, [.. reasons, "Certification exceeds the calibrated comfort ceiling. Any lower or unavailable source does not override that restriction."], sources);
        var dimensions = evidence.Where(e => !e.Failed).SelectMany(e => e.Dimensions).GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.Max(d => d.Value));
        foreach (var (dimension, severity) in dimensions)
        {
            var tolerance = CalibrationEngine.Tolerance(profile, dimension, cache);
            reasons.Add($"{dimension}: reported {severity}/4; tolerance {tolerance}/4.");
            if (severity > tolerance) return new(Decision.Block, 90, age, [.. reasons, $"{dimension} exceeds your tolerance."], sources);
        }
        if (evidence.Any(e => e.Failed))
            return new(Decision.Review, 25, age, [.. reasons, "An enabled provider failed. Review before allowing."], sources);
        if (!childrensPass && ageValues.Length > 1 && ageValues.Max() - ageValues.Min() >= 3)
            return new(Decision.Review, 35, age, [.. reasons, "Certification sources disagree significantly."], sources);
        if (age is null)
            return new(Decision.Review, 20, null, [.. reasons, "Missing or unrecognized certification. Unknown does not mean safe."], sources);
        // Explicit advanced constraints cannot be verified without the corresponding evidence.
        if (profile.Tolerances.Keys.Any(d => !dimensions.ContainsKey(d)))
            return new(Decision.Review, 45, age, [.. reasons, "A dimension you explicitly constrained has no advisory evidence."], sources);
        var confidence = dimensions.Count == Enum.GetValues<Dimension>().Length ? 95 : age == 0 ? 85 : 70;
        if (evidence.Count > assessment.Evidence.Count) confidence = Math.Min(confidence, 70);
        if (profile.Age < 8 && dimensions.Count < 3 && age != 0)
            return new(Decision.Review, 45, age, [.. reasons, "Limited advisory detail for a young profile."], sources);
        return new(Decision.Allow, confidence, age, [.. reasons, "Within the selected ceiling. Recommended for the draft allowlist; apply the reviewed profile to grant access. Unreported content dimensions remain unknown."], sources);
    }
}
