namespace KidGuard.Core;

public enum Decision { Review, Allow, Block }
public enum OverrideKind { None, Allow, Block, AlwaysAllow, AlwaysBlock }
public enum Approach { Conservative, Standard, Permissive }
public enum NewMediaBehavior { Review, Automatic, Block }
public enum Dimension { Violence, Fear, SexualContent, Nudity, Profanity, Substances, MatureThemes, DeathGrief, DisturbingImagery }
public enum MediaKind { Movie, Series, Season, Episode }
public record Advisory(string Source, string? Rating, Dictionary<Dimension, int> Dimensions, string Note, bool Failed = false);
public record LibraryItem(Guid Id, string Title, MediaKind Kind, Guid? ParentId, string? Rating, int? Year,
    string[] Genres, string[] Tags, string? Overview, Dictionary<string, string> ProviderIds, Guid[] Ancestors);
public record Assessment(LibraryItem Item, List<Advisory> Evidence, DateTimeOffset RetrievedAt, string Fingerprint, int Version = 1);
public record Recommendation(Decision Decision, int Confidence, int? SuggestedAge, string[] Reasons, string[] Sources);
public record CalibrationAnswer(Guid ItemId, Decision Answer);
public sealed class ChildProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string Label { get; set; } = "Child profile";
    public string? NewUsername { get; set; }
    public int Age { get; set; } = 8;
    public int MaturityOffset { get; set; }
    public Approach Approach { get; set; } = Approach.Conservative;
    public NewMediaBehavior NewMedia { get; set; } = NewMediaBehavior.Review;
    public Dictionary<Dimension, int> Tolerances { get; set; } = [];
    public List<CalibrationAnswer> Calibration { get; set; } = [];
    public Dictionary<Guid, OverrideKind> Overrides { get; set; } = [];
    public Dictionary<Guid, Recommendation> Recommendations { get; set; } = [];
    public HashSet<Guid> Approved { get; set; } = [];
    public HashSet<Guid> PendingNew { get; set; } = [];
    public HashSet<Guid> Navigation { get; set; } = [];
    public string? ActiveTag { get; set; }
    public bool Applied { get; set; }
    public bool AllowDownloads { get; set; }
    public bool AllowRemoteAccess { get; set; } = true;
    public bool AllowTranscoding { get; set; } = true;
    public Guid[]? EnabledFolders { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset? LastApplied { get; set; }
}
public sealed class Settings
{
    public string TmdbToken { get; set; } = "";
    public bool ExternalEnabled { get; set; }
    public string Country { get; set; } = "US";
    public int CacheDays { get; set; } = 30;
    public int AutoConfidence { get; set; } = 90;
    public bool FamilyRequiresAll { get; set; }
}
public record AuditEntry(DateTimeOffset At, Guid ProfileId, string Action, int Added = 0, int Removed = 0);
public sealed class State
{
    public int SchemaVersion { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public List<ChildProfile> Profiles { get; set; } = [];
    public Dictionary<Guid, Assessment> Cache { get; set; } = [];
    public Dictionary<Guid, bool> FamilyOverrides { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}
