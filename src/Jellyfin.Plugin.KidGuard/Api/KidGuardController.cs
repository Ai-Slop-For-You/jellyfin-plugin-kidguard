using KidGuard.Core;
using Jellyfin.Plugin.KidGuard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KidGuard.Api;
[ApiController]
[Route("KidGuard")]
[Authorize(Policy = "RequiresElevation")]
public sealed class KidGuardController(Manager manager) : ControllerBase
{
    [HttpGet("Dashboard")]
    public async Task<ActionResult<object>> Dashboard()
    {
        await manager.ReconcileDeletedUsers().ConfigureAwait(false);
        var state = manager.Snapshot();
        var settings = Json.Clone(state.Settings); settings.TmdbToken = "";
        return Ok(new { Profiles = state.Profiles.Select(p => new { p.Id, p.Label, p.Age, p.Approach, p.Applied, p.Revision,
            Allowed = p.Approved.Count, RecommendedAllowed = p.Recommendations.Count(r => r.Value.Decision == Decision.Allow),
            Blocked = p.Recommendations.Count(r => r.Value.Decision == Decision.Block), Review = p.Recommendations.Count(r => r.Value.Decision == Decision.Review), Overrides = p.Overrides.Count }),
            Settings = settings, HasTmdbToken = state.Settings.TmdbToken.Length > 0, Progress = manager.Progress,
            RecoveryRequired = manager.RecoveryRequired, Audit = state.Audit.TakeLast(30).Reverse(), Total = state.Cache.Count });
    }
    [HttpGet("Users")] public ActionResult<object> Users() => Ok(manager.Users());
    [HttpGet("Profiles/{id:guid}")] public ActionResult<ChildProfile> Profile(Guid id) => manager.Snapshot().Profiles.FirstOrDefault(p => p.Id == id) is { } p ? Ok(p) : NotFound();
    [HttpPost("Profiles")] public async Task<ActionResult<ChildProfile>> Save(ChildProfile profile) => await Safe(async () => (ActionResult<ChildProfile>)Ok(await manager.SaveProfile(profile).ConfigureAwait(false))).ConfigureAwait(false);
    [HttpGet("Profiles/{id:guid}/Library")]
    public ActionResult<object> Library(Guid id, string? search = null, string? status = null, string? kind = null, bool unrated = false,
        bool lowConfidence = false, bool manual = false, int? ageMin = null, int? ageMax = null, string? rating = null, string sort = "title", int start = 0, int limit = 100, Guid? parent = null)
    {
        var state = manager.Snapshot(); var p = state.Profiles.FirstOrDefault(p => p.Id == id); if (p is null) return NotFound();
        var planned = ApprovalPlanner.Build(p, state.Cache);
        var rows = state.Cache.Values.Select(a => new { Item = a.Item, Recommendation = p.Recommendations.GetValueOrDefault(a.Item.Id),
            Override = p.Overrides.GetValueOrDefault(a.Item.Id), Applied = p.Approved.Contains(a.Item.Id) || p.Navigation.Contains(a.Item.Id), Proposed = planned.Playable.Contains(a.Item.Id) || planned.Navigation.Contains(a.Item.Id), Evidence = a.Evidence });
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(r => r.Item.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (Enum.TryParse<Decision>(status, true, out var decision)) rows = rows.Where(r => r.Recommendation?.Decision == decision);
        if (Enum.TryParse<MediaKind>(kind, true, out var mediaKind)) rows = rows.Where(r => r.Item.Kind == mediaKind);
        else if (kind == "titles") rows = rows.Where(r => r.Item.Kind is MediaKind.Movie or MediaKind.Series);
        if (parent.HasValue) rows = rows.Where(r => r.Item.Ancestors.Contains(parent.Value));
        if (unrated) rows = rows.Where(r => Ratings.Age(r.Item.Rating) is null);
        if (lowConfidence) rows = rows.Where(r => r.Recommendation?.Confidence < 80);
        if (manual) rows = rows.Where(r => r.Override != OverrideKind.None);
        if (ageMin.HasValue) rows = rows.Where(r => r.Recommendation?.SuggestedAge >= ageMin.Value);
        if (ageMax.HasValue) rows = rows.Where(r => r.Recommendation?.SuggestedAge <= ageMax.Value);
        if (!string.IsNullOrWhiteSpace(rating)) rows = rows.Where(r => string.Equals(r.Item.Rating, rating, StringComparison.OrdinalIgnoreCase));
        rows = sort switch { "confidence" => rows.OrderBy(r => r.Recommendation?.Confidence), "age" => rows.OrderBy(r => r.Recommendation?.SuggestedAge), "status" => rows.OrderBy(r => r.Recommendation?.Decision), _ => rows.OrderBy(r => r.Item.Title) };
        var all = rows.ToArray();
        return Ok(new { Items = all.Skip(Math.Max(0, start)).Take(Math.Clamp(limit, 1, 250)), Total = all.Length, p.Revision,
            ProposedPlayable = planned.Playable.Count, AppliedPlayable = p.Approved.Count });
    }
    [HttpGet("Calibration")] public ActionResult<object> Calibration() => Ok(CalibrationEngine.SelectExamples(manager.Snapshot().Cache.Values));
    [HttpPost("Profiles/{id:guid}/Overrides")]
    public Task<ActionResult> Overrides(Guid id, BatchRequest request) => Safe(async () => { await manager.Overrides(id, request.Items, request.Choice, request.Revision).ConfigureAwait(false); return (ActionResult)NoContent(); });
    [HttpPost("Analyze")] public ActionResult Analyze([FromQuery] Guid? refresh = null) { try { manager.StartScan(refresh); return Accepted(); } catch (InvalidOperationException e) { return Conflict(new { Error = e.Message }); } }
    [HttpPost("Analyze/Cancel")] public ActionResult Cancel() { manager.CancelScan(); return NoContent(); }
    [HttpPost("Profiles/{id:guid}/Apply")]
    public Task<ActionResult> Apply(Guid id, ApplyRequest request) => Safe(async () =>
    {
        if (!request.Confirm) return BadRequest(new { Error = "Explicit parent approval is required." });
        await manager.Apply(id, request.Revision, request.NewPassword, CancellationToken.None).ConfigureAwait(false); return (ActionResult)NoContent();
    });
    [HttpPost("Profiles/{id:guid}/Undo")]
    public Task<ActionResult> Undo(Guid id, ConfirmRequest request) => Safe(async () =>
    {
        if (!request.Confirm) return BadRequest(new { Error = "Confirm rollback. Initial undo restores the original user's access policy." });
        await manager.Undo(id, CancellationToken.None).ConfigureAwait(false); return (ActionResult)NoContent();
    });
    [HttpPost("Settings")]
    public Task<ActionResult> Settings(SettingsRequest request) => Safe(async () => { await manager.SaveSettings(request.Settings, request.ReplaceToken).ConfigureAwait(false); return (ActionResult)NoContent(); });
    [HttpGet("Family")]
    public ActionResult<object> Family()
    {
        var state = manager.Snapshot(); var ids = manager.FamilyIds(state);
        return Ok(state.Cache.Values.Where(a => a.Item.Kind is MediaKind.Movie or MediaKind.Series or MediaKind.Episode).Select(a => new { a.Item.Id, a.Item.Title, a.Item.Kind, Allowed = ids.Contains(a.Item.Id), Manual = state.FamilyOverrides.ContainsKey(a.Item.Id) }));
    }
    [HttpPost("Family")]
    public Task<ActionResult> Family(FamilyRequest request) => Safe(async () => { await manager.SetFamily(request.Items, request.Allowed, CancellationToken.None).ConfigureAwait(false); return (ActionResult)NoContent(); });
    private async Task<T> Safe<T>(Func<Task<T>> operation) where T : ActionResult
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ArgumentException e) { return (T)(ActionResult)BadRequest(new { Error = e.Message }); }
        catch (InvalidOperationException e) { return (T)(ActionResult)Conflict(new { Error = e.Message }); }
    }
    private async Task<ActionResult<ChildProfile>> Safe(Func<Task<ActionResult<ChildProfile>>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ArgumentException e) { return BadRequest(new { Error = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { Error = e.Message }); }
    }
}
public record BatchRequest(Guid[] Items, OverrideKind Choice, long Revision);
public record ApplyRequest(long Revision, bool Confirm, string? NewPassword);
public record ConfirmRequest(bool Confirm);
public record SettingsRequest(Settings Settings, bool ReplaceToken);
public record FamilyRequest(Guid[] Items, bool? Allowed);
