using KidGuard.Core;
using Jellyfin.Plugin.KidGuard.Providers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KidGuard.Services;
public sealed class Journal
{
    public Guid ProfileId { get; set; }
    public Guid UserId { get; set; }
    public ChildProfile Before { get; set; } = new();
    public UserPolicy BeforePolicy { get; set; } = new();
    public string StagedTag { get; set; } = "";
    public Guid[] TaggedItems { get; set; } = [];
    public bool Pending { get; set; }
    public ChildProfile? BeforeApprovedSettings { get; set; }
}
public sealed class DurableState
{
    public State Data { get; set; } = new();
    public Dictionary<Guid, Journal> Undo { get; set; } = [];
    public Journal? Pending { get; set; }
    public Dictionary<Guid, ChildProfile> ApprovedSettings { get; set; } = [];
    public Dictionary<string, HashSet<Guid>> OrphanTags { get; set; } = [];
    public bool RefreshFamilyPending { get; set; }
}
public record ScanProgress(bool Running, int Complete, int Total, string? Error);
public sealed class Manager
{
    private readonly LibraryAdapter _library;
    private readonly ContentAnalysis _analysis;
    private readonly ILogger<Manager> _logger;
    private readonly AtomicStore<DurableState> _store;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private DurableState _state;
    private CancellationTokenSource? _scanCancel;
    private Task? _scanTask;
    private ScanProgress _progress = new(false, 0, 0, null);
    public Manager(IApplicationPaths paths, LibraryAdapter library, ContentAnalysis analysis, ILogger<Manager> logger)
    {
        _library = library; _analysis = analysis; _logger = logger;
        _store = new(Path.Combine(paths.DataPath, "kidguard", "state.json"));
        // Corrupt state fails plugin initialization rather than silently removing restrictions.
        _state = _store.Read();
        if (_state.Data.SchemaVersion != 1) throw new InvalidDataException("Unsupported KidGuard state schema; restore a compatible backup.");
    }
    public State Snapshot() { lock (_sync) return Json.Clone(_state.Data); }
    public ScanProgress Progress { get { lock (_sync) return _progress; } }
    public bool RecoveryRequired { get { lock (_sync) return _state.Pending is not null; } }
    public object[] Users() => _library.Users();
    public async Task ReconcileDeletedUsers(CancellationToken cancellation = default)
    {
        await _write.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                // Match immutable user IDs, never usernames. Unapplied drafts without an account remain.
                // Database lookup errors abort reconciliation instead of treating an outage as deletion.
                var missing = _state.Data.Profiles.Where(p => p.UserId.HasValue && !_library.UserExists(p.UserId.Value)).ToArray();
                foreach (var profile in missing)
                {
                    void Queue(string? tag, IEnumerable<Guid> ids)
                    {
                        if (tag is null) return;
                        if (!_state.OrphanTags.TryGetValue(tag, out var items)) _state.OrphanTags[tag] = items = [];
                        items.UnionWith(ids);
                    }
                    Queue(profile.ActiveTag, profile.Approved.Union(profile.Navigation));
                    var journals = new[] { _state.Undo.GetValueOrDefault(profile.Id), _state.Pending?.ProfileId == profile.Id ? _state.Pending : null };
                    foreach (var journal in journals)
                    {
                        if (journal is null) continue;
                        Queue(journal.StagedTag, journal.TaggedItems);
                        Queue(journal.Before.ActiveTag, journal.Before.Approved.Union(journal.Before.Navigation));
                    }
                    _state.Data.Profiles.Remove(profile);
                    _state.ApprovedSettings.Remove(profile.Id);
                    _state.Undo.Remove(profile.Id);
                    if (_state.Pending?.ProfileId == profile.Id) _state.Pending = null;
                    Audit(profile.Id, "Removed profile because its linked Jellyfin user was deleted.", removed: profile.Approved.Count);
                }
                if (missing.Length > 0)
                {
                    _state.RefreshFamilyPending = true;
                    Persist(); // Save removal and cleanup intent together before touching metadata.
                }
            }
            KeyValuePair<string, HashSet<Guid>>[] cleanup;
            lock (_sync) cleanup = _state.OrphanTags.ToArray();
            foreach (var (tag, items) in cleanup)
            {
                try
                {
                    foreach (var id in items) await _library.Tag(id, tag, null, cancellation).ConfigureAwait(false);
                    lock (_sync) { _state.OrphanTags.Remove(tag); Persist(); }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                { _logger.LogWarning(e, "KidGuard will retry deleted-profile tag cleanup"); }
            }
            bool refresh; lock (_sync) refresh = _state.RefreshFamilyPending;
            if (refresh)
            {
                try
                {
                    await UpdateFamilyTags(cancellation).ConfigureAwait(false);
                    lock (_sync) { _state.RefreshFamilyPending = false; Persist(); }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                { _logger.LogWarning(e, "KidGuard will retry the family catalog update after user deletion"); }
            }
        }
        finally { _write.Release(); }
    }
    public AccessSnapshot? AccessFor(Guid userId)
    {
        lock (_sync)
        {
            var p = _state.Data.Profiles.FirstOrDefault(p => p.UserId == userId && (p.Applied || _state.Pending?.ProfileId == p.Id));
            // Sets are replaced as a unit on commit and never mutated after publication.
            return p is null ? null : new(p.Approved, p.Navigation);
        }
    }
    private ChildProfile Find(Guid id) => _state.Data.Profiles.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("Profile not found.");
    private void Persist()
    {
        try { _store.Write(_state); }
        catch { _state = _store.Read(); throw; } // Restore the last durable journal on a failed commit.
    }
    private void Audit(Guid id, string message, int added = 0, int removed = 0)
    {
        _state.Data.Audit.Add(new(DateTimeOffset.UtcNow, id, message, added, removed));
        if (_state.Data.Audit.Count > 500) _state.Data.Audit.RemoveRange(0, _state.Data.Audit.Count - 500);
    }
    private void Recalculate(ChildProfile profile)
    {
        var engine = new RecommendationEngine();
        profile.Recommendations = _state.Data.Cache.ToDictionary(p => p.Key, p => engine.Evaluate(profile, p.Value, _state.Data.Cache));
        foreach (var id in profile.PendingNew)
        {
            if (profile.NewMedia == NewMediaBehavior.Automatic || profile.Overrides.GetValueOrDefault(id) != OverrideKind.None || !profile.Recommendations.TryGetValue(id, out var rec)) continue;
            profile.Recommendations[id] = rec with { Decision = profile.NewMedia == NewMediaBehavior.Block ? Decision.Block : Decision.Review,
                Reasons = [.. rec.Reasons, "New media is excluded until explicit parent approval."] };
        }
    }
    public async Task<ChildProfile> SaveProfile(ChildProfile input)
    {
        if (input.Age is < 0 or > 17 || input.MaturityOffset is < -3 or > 3 || string.IsNullOrWhiteSpace(input.Label) || input.Label.Length > 80
            || !Enum.IsDefined(input.Approach) || !Enum.IsDefined(input.NewMedia) || input.Tolerances.Any(t => !Enum.IsDefined(t.Key) || t.Value is < 0 or > 4)
            || input.Calibration.Count > 12 || input.Calibration.Any(a => !Enum.IsDefined(a.Answer))) throw new ArgumentException("Invalid profile settings.");
        await _write.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_state.Pending is not null) throw new InvalidOperationException("Recover the interrupted apply first.");
                var existing = _state.Data.Profiles.FirstOrDefault(p => p.Id == input.Id);
                if (existing is not null && existing.Revision != input.Revision) throw new InvalidOperationException("Draft changed. Reload before saving.");
                if (input.UserId.HasValue && _library.Policy(input.UserId.Value).IsAdministrator) throw new ArgumentException("Administrator accounts cannot be child profiles.");
                if (_state.Data.Profiles.Any(p => p.Id != input.Id && p.UserId.HasValue && p.UserId == input.UserId)) throw new ArgumentException("A user can only map to one child profile.");
                if (existing?.Applied == true && existing.UserId != input.UserId) throw new ArgumentException("An applied profile cannot be remapped. Undo its initial apply first.");
                if (input.UserId is null && string.IsNullOrWhiteSpace(input.NewUsername)) throw new ArgumentException("Select a user or enter a new username.");
                var p = existing ?? new ChildProfile { Id = input.Id };
                p.UserId = input.UserId; p.NewUsername = input.NewUsername?.Trim(); p.Label = input.Label.Trim(); p.Age = input.Age;
                p.MaturityOffset = input.MaturityOffset; p.Approach = input.Approach; p.NewMedia = input.NewMedia;
                p.Tolerances = new(input.Tolerances); p.Calibration = [.. input.Calibration];
                p.AllowDownloads = input.AllowDownloads; p.AllowRemoteAccess = input.AllowRemoteAccess; p.AllowTranscoding = input.AllowTranscoding;
                p.EnabledFolders = input.EnabledFolders;
                if (existing is null) _state.Data.Profiles.Add(p);
                Recalculate(p); p.Revision++; Audit(p.Id, "Draft settings saved; access unchanged"); Persist(); return Json.Clone(p);
            }
        }
        finally { _write.Release(); }
    }
    public async Task Overrides(Guid profileId, Guid[] items, OverrideKind choice, long revision)
    {
        if (items.Length is < 1 or > 10000 || !Enum.IsDefined(choice)) throw new ArgumentException("Invalid batch.");
        await _write.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                var p = Find(profileId); if (p.Revision != revision) throw new InvalidOperationException("Draft changed. Reload before editing.");
                if (items.Any(id => !_state.Data.Cache.ContainsKey(id))) throw new ArgumentException("An item is no longer in the analyzed library.");
                foreach (var id in items) { if (choice == OverrideKind.None) p.Overrides.Remove(id); else p.Overrides[id] = choice; }
                Recalculate(p); p.Revision++; Audit(p.Id, $"Draft: {choice} on {items.Length} items; access unchanged"); Persist();
            }
        }
        finally { _write.Release(); }
    }
    public async Task SaveSettings(Settings settings, bool replaceToken)
    {
        if (settings.CacheDays is < 1 or > 365 || settings.AutoConfidence is < 90 or > 100 || !new[] { "US", "GB", "DE" }.Contains(settings.Country))
            throw new ArgumentException("Unsupported country or cache/confidence value.");
        await _write.WaitAsync().ConfigureAwait(false);
        try { lock (_sync) { if (!replaceToken) settings.TmdbToken = _state.Data.Settings.TmdbToken; _state.Data.Settings = settings; Persist(); } }
        finally { _write.Release(); }
    }
    public void StartScan(Guid? refresh = null)
    {
        lock (_sync)
        {
            if (_progress.Running) throw new InvalidOperationException("An analysis is already running.");
            _scanCancel?.Dispose(); _scanCancel = new(); var token = _scanCancel.Token;
            _progress = new(true, 0, 0, null);
            _scanTask = Task.Run(() => Scan(refresh, token), CancellationToken.None);
        }
    }
    public void CancelScan() { lock (_sync) _scanCancel?.Cancel(); }
    public async Task Stop() { CancelScan(); Task? scan; lock (_sync) scan = _scanTask; if (scan is not null) await scan.ConfigureAwait(false); }
    private async Task Scan(Guid? refresh, CancellationToken cancellation)
    {
        try
        {
            var before = Snapshot();
            var library = _library.Scan(cancellation).ToArray();
            var results = new System.Collections.Concurrent.ConcurrentDictionary<Guid, Assessment>();
            lock (_sync) _progress = new(true, 0, library.Length, null);
            await Parallel.ForEachAsync(library, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellation }, async (item, token) =>
            {
                results[item.Id] = await _analysis.Analyze(item, before.Settings, item.Id == refresh ? null : before.Cache.GetValueOrDefault(item.Id), token).ConfigureAwait(false);
                lock (_sync) _progress = _progress with { Complete = _progress.Complete + 1 };
            }).ConfigureAwait(false);
            await _write.WaitAsync(cancellation).ConfigureAwait(false);
            Guid[] automatic;
            try
            {
                lock (_sync)
                {
                    var fresh = results.Keys.Except(_state.Data.Cache.Keys).ToArray();
                    _state.Data.Cache = results.ToDictionary();
                    foreach (var p in _state.Data.Profiles)
                    {
                        if (p.Applied) p.PendingNew.UnionWith(fresh);
                        foreach (var key in p.Overrides.Where(x => x.Value is OverrideKind.Allow or OverrideKind.Block).Select(x => x.Key).ToArray()) p.Overrides.Remove(key);
                        Recalculate(p); p.Revision++;
                    }
                    automatic = _state.Data.Profiles.Where(p => p.Applied && _state.ApprovedSettings.GetValueOrDefault(p.Id)?.NewMedia == NewMediaBehavior.Automatic).Select(p => p.Id).ToArray();
                    Persist();
                }
            }
            finally { _write.Release(); }
            // Only fresh leaf items; never apply unrelated draft edits or inherit container overrides.
            foreach (var id in automatic)
                await AutoApprove(id, cancellation).ConfigureAwait(false);
            lock (_sync) _progress = _progress with { Running = false };
        }
        catch (OperationCanceledException) { lock (_sync) _progress = _progress with { Running = false, Error = "Analysis canceled; previous results retained." }; }
        catch (Exception e) { _logger.LogError(e, "KidGuard library analysis failed"); lock (_sync) _progress = _progress with { Running = false, Error = "Analysis failed. Check the Jellyfin server log; access is unchanged." }; }
    }
    private async Task AutoApprove(Guid id, CancellationToken cancellation)
    {
        var state = Snapshot(); var p = state.Profiles.FirstOrDefault(x => x.Id == id);
        if (p is null) return; // The linked user may have been deleted while analysis completed.
        ChildProfile approvedSettings;
        lock (_sync)
        {
            if (!_state.ApprovedSettings.TryGetValue(id, out var settings)) return;
            approvedSettings = Json.Clone(settings);
        }
        var engine = new RecommendationEngine();
        var additions = p.PendingNew.Where(state.Cache.ContainsKey).Where(i => state.Cache[i].Item.Kind is MediaKind.Movie or MediaKind.Episode)
            .Where(i => { var rec = engine.Evaluate(approvedSettings, state.Cache[i], state.Cache); return rec.Decision == Decision.Allow && rec.Confidence >= state.Settings.AutoConfidence; })
            .Where(i => !p.Overrides.ContainsKey(i) && !state.Cache[i].Item.Ancestors.Any(p.Overrides.ContainsKey)).ToHashSet();
        if (additions.Count == 0) return;
        var playable = p.Approved.Union(additions).ToHashSet();
        await Apply(id, p.Revision, null, cancellation, new(playable, p.Navigation.Union(additions.SelectMany(i => state.Cache[i].Item.Ancestors)).ToHashSet())).ConfigureAwait(false);
    }
    public async Task Apply(Guid id, long revision, string? password, CancellationToken cancellation, ApprovalPlan? automatic = null)
    {
        await _write.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ChildProfile p; ApprovalPlan plan; Journal journal;
            lock (_sync)
            {
                if (_state.Pending is not null) throw new InvalidOperationException("An interrupted apply needs recovery.");
                p = Json.Clone(Find(id));
                if (p.Revision != revision) throw new InvalidOperationException("Recommendations changed. Reload and review before applying.");
                if (_progress.Running && automatic is null) throw new InvalidOperationException("Wait for analysis to finish before approving.");
                if (_state.Data.Cache.Count == 0) throw new InvalidOperationException("Analyze the library before applying.");
                plan = automatic ?? ApprovalPlanner.Build(p, _state.Data.Cache);
            }
            if (p.UserId is null)
            {
                p.UserId = await _library.Create(p.NewUsername!, password ?? "").ConfigureAwait(false);
                lock (_sync) { Find(id).UserId = p.UserId; Persist(); }
            }
            var oldPolicy = _library.Policy(p.UserId.Value);
            if (oldPolicy.IsAdministrator) throw new ArgumentException("Refusing to restrict an administrator.");
            if (automatic is not null && oldPolicy.IsDisabled) return;
            var tag = ApprovalPlanner.Tag(id, Guid.NewGuid());
            journal = new() { ProfileId = id, UserId = p.UserId.Value, Before = Json.Clone(p), BeforePolicy = oldPolicy, StagedTag = tag, TaggedItems = plan.Playable.Union(plan.Navigation.Where(i => _library.Get(i) is MediaBrowser.Controller.Entities.TV.Series or MediaBrowser.Controller.Entities.TV.Season)).ToArray(), Pending = true };
            lock (_sync)
            {
                journal.BeforeApprovedSettings = _state.ApprovedSettings.GetValueOrDefault(id);
                _state.Pending = journal;
                // Persist intent before any changes. Guard denies all requests while pending.
                Persist();
            }
            var disabled = Json.Clone(oldPolicy); disabled.IsDisabled = true;
            await _library.SetPolicy(p.UserId.Value, disabled).ConfigureAwait(false);
            try
            {
                foreach (var itemId in journal.TaggedItems) await _library.Tag(itemId, null, tag, cancellation).ConfigureAwait(false);
                var next = automatic is null ? LibraryAdapter.ChildPolicy(oldPolicy, p, tag) : Json.Clone(oldPolicy);
                next.AllowedTags = [tag];
                // Existing folder ACLs remain the security boundary; parental filters are replaced after explicit approval.
                await _library.SetPolicy(p.UserId.Value, next).ConfigureAwait(false);
                lock (_sync)
                {
                    var live = Find(id); var prior = live.Approved;
                    live.Approved = plan.Playable; live.Navigation = plan.Navigation; live.ActiveTag = tag; live.Applied = true;
                    live.PendingNew.ExceptWith(plan.Playable);
                    live.LastApplied = DateTimeOffset.UtcNow; live.Revision++;
                    if (automatic is null)
                    {
                        var approvedConfig = Json.Clone(live); approvedConfig.Recommendations.Clear(); approvedConfig.Approved.Clear(); approvedConfig.Navigation.Clear();
                        _state.ApprovedSettings[id] = approvedConfig;
                    }
                    _state.Undo[id] = journal; journal.Pending = false; _state.Pending = null;
                    Audit(id, automatic is null ? "Parent approved and applied library" : "Opt-in automatic approval of new high-confidence leaf items", plan.Playable.Except(prior).Count(), prior.Except(plan.Playable).Count());
                    Persist();
                }
                // Old generation is now inert; remove just the tags we own, preserving other metadata.
                if (p.ActiveTag is not null)
                    foreach (var itemId in p.Approved.Union(p.Navigation)) await _library.Tag(itemId, p.ActiveTag, null, CancellationToken.None).ConfigureAwait(false);
                await UpdateFamilyTags(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // If commit completed, cleanup failure must not revert a successful policy.
                bool pending; lock (_sync) pending = _state.Pending is not null;
                if (pending)
                {
                    var failClosed = _library.Policy(p.UserId.Value); failClosed.IsDisabled = true;
                    await _library.SetPolicy(p.UserId.Value, failClosed).ConfigureAwait(false);
                }
                throw;
            }
        }
        finally { _write.Release(); }
    }
    public async Task RecoverOnStart()
    {
        await ReconcileDeletedUsers().ConfigureAwait(false);
        Journal? pending; lock (_sync) pending = _state.Pending;
        if (pending is null) return;
        var policy = _library.Policy(pending.UserId); policy.IsDisabled = true;
        await _library.SetPolicy(pending.UserId, policy).ConfigureAwait(false);
        _logger.LogError("KidGuard interrupted apply detected. Affected account disabled until administrator recovery.");
    }
    public async Task Undo(Guid id, CancellationToken cancellation)
    {
        await _write.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            Journal journal; ChildProfile current;
            lock (_sync)
            {
                journal = _state.Pending?.ProfileId == id ? _state.Pending : _state.Undo.GetValueOrDefault(id) ?? throw new InvalidOperationException("No previous apply to restore.");
                current = Json.Clone(Find(id)); _state.Pending = journal; Persist();
            }
            var disabled = _library.Policy(journal.UserId); disabled.IsDisabled = true;
            await _library.SetPolicy(journal.UserId, disabled).ConfigureAwait(false);
            if (journal.Before.ActiveTag is not null)
                foreach (var item in journal.Before.Approved.Union(journal.Before.Navigation)) await _library.Tag(item, null, journal.Before.ActiveTag, cancellation).ConfigureAwait(false);
            foreach (var item in journal.TaggedItems) await _library.Tag(item, journal.StagedTag, null, cancellation).ConfigureAwait(false);
            await _library.SetPolicy(journal.UserId, journal.BeforePolicy).ConfigureAwait(false);
            lock (_sync)
            {
                _state.Data.Profiles.RemoveAll(p => p.Id == id);
                var restored = Json.Clone(journal.Before); restored.Revision = current.Revision + 1;
                _state.Data.Profiles.Add(restored); _state.Pending = null; _state.Undo.Remove(id);
                if (journal.BeforeApprovedSettings is null) _state.ApprovedSettings.Remove(id); else _state.ApprovedSettings[id] = journal.BeforeApprovedSettings;
                Audit(id, "Restored policy and profile before last apply"); Persist();
            }
            await UpdateFamilyTags(cancellation).ConfigureAwait(false);
        }
        finally { _write.Release(); }
    }
    public HashSet<Guid> FamilyIds(State state)
    {
        var active = state.Profiles.Where(p => p.Applied).ToArray();
        var ids = active.Length == 0 ? [] : active[0].Approved.ToHashSet();
        foreach (var p in active.Skip(1)) { if (state.Settings.FamilyRequiresAll) ids.IntersectWith(p.Approved); else ids.UnionWith(p.Approved); }
        foreach (var (id, allowed) in state.FamilyOverrides) { if (allowed) ids.Add(id); else ids.Remove(id); }
        return ids;
    }
    public async Task SetFamily(Guid[] items, bool? allowed, CancellationToken cancellation)
    {
        await _write.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                foreach (var id in items) { if (!_state.Data.Cache.ContainsKey(id)) throw new ArgumentException("Unknown item."); if (allowed.HasValue) _state.Data.FamilyOverrides[id] = allowed.Value; else _state.Data.FamilyOverrides.Remove(id); }
                Persist();
            }
            await UpdateFamilyTags(cancellation).ConfigureAwait(false);
        }
        finally { _write.Release(); }
    }
    private async Task UpdateFamilyTags(CancellationToken cancellation)
    {
        var state = Snapshot(); var ids = FamilyIds(state);
        foreach (var id in state.Cache.Keys)
        {
            var item = _library.Get(id); if (item is null) continue;
            if (ids.Contains(id) && !item.Tags.Contains("KidSafe")) await _library.Tag(id, null, "KidSafe", cancellation).ConfigureAwait(false);
            else if (!ids.Contains(id) && item.Tags.Contains("KidSafe")) await _library.Tag(id, "KidSafe", null, cancellation).ConfigureAwait(false);
        }
    }
}
