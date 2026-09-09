using System.Text.Json;
using System.Text.Json.Nodes;
using KidGuard.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Jellyfin.Plugin.KidGuard.Services;
/// <summary>Defense in depth against inherited-tag admission of unapproved episodes.</summary>
public sealed class SnapshotGuard(Manager manager, LibraryAdapter library, IOptions<JsonOptions> options) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => int.MinValue + 100;
    private static readonly HashSet<string> ItemKeys = new(StringComparer.OrdinalIgnoreCase)
    { "itemId", "id", "ids", "itemIds", "parentId", "seriesId", "seasonId", "mediaSourceId", "audioId", "videoId" };
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var userId)) { await next().ConfigureAwait(false); return; }
        var access = manager.AccessFor(userId);
        if (access is null) { await next().ConfigureAwait(false); return; }
        if (manager.RecoveryRequired) { context.Result = new StatusCodeResult(403); return; }
        var controller = context.RouteData.Values.GetValueOrDefault("controller")?.ToString() ?? "";
        // These paths can introduce content outside the approved movie/episode catalog.
        if (new[] { "LiveTv", "Channels", "SyncPlay", "Dlna", "InstantMix" }.Contains(controller, StringComparer.OrdinalIgnoreCase))
        { context.Result = new StatusCodeResult(403); return; }
        bool Allowed(Guid id)
        {
            if (id == Guid.Empty) return true;
            var item = library.Get(id);
            if (item is null) return true; // Native endpoint handles unknown/non-library IDs.
            if (item is Movie or Episode) return access.Playable.Contains(id);
            if (item is Series or Season) return access.Navigation.Contains(id);
            // Non-video content is out of scope. Navigation folders are safe; native policies still apply.
            return item.IsFolder || item is IItemByName;
        }
        foreach (var pair in context.HttpContext.Request.RouteValues.Select(p => (p.Key, Value: p.Value?.ToString()))
            .Concat(context.HttpContext.Request.Query.Select(p => (p.Key, Value: (string?)p.Value.ToString()))))
        {
            if (pair.Key.Equals("userId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(pair.Value, out var requestedUser) && requestedUser != userId)
            { context.Result = new StatusCodeResult(403); return; }
            if (!ItemKeys.Contains(pair.Key)) continue;
            foreach (var part in (pair.Value ?? "").Split(','))
                if (Guid.TryParse(part, out var id) && !Allowed(id)) { context.Result = new NotFoundResult(); return; }
        }
        // PlaybackInfo and playback reporting can carry the media source in a bound request body.
        foreach (var value in context.ActionArguments.Values.Where(v => v is not null))
        {
            var type = value!.GetType();
            if (type.IsPrimitive || value is string or Guid || type.IsEnum) continue;
            foreach (var property in type.GetProperties().Where(p => ItemKeys.Contains(p.Name) && p.GetIndexParameters().Length == 0))
                if (Guid.TryParse(property.GetValue(value)?.ToString(), out var id) && !Allowed(id)) { context.Result = new NotFoundResult(); return; }
        }
        var executed = await next().ConfigureAwait(false);
        if (executed.Result is ObjectResult result && result.Value is not null && (result.StatusCode is null or >= 200 and < 300))
        {
            // Use host serializer options; preserve native API casing, enum formatting and DTO shape.
            var node = JsonSerializer.SerializeToNode(result.Value, result.Value.GetType(), options.Value.JsonSerializerOptions);
            if (!Prune(node, Allowed)) executed.Result = new NotFoundResult();
            else result.Value = node;
        }
    }
    public static bool Prune(JsonNode? node, Func<Guid, bool> allowed)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "Id", "ItemId", "id", "itemId" })
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) && !allowed(id)) return false;
            foreach (var property in obj.ToArray())
                if (!Prune(property.Value, allowed)) obj[property.Key] = null;
            foreach (var itemsKey in new[] { "Items", "SearchHints", "items", "searchHints" })
                if (obj[itemsKey] is JsonArray array && array.Count == 0)
                    foreach (var countKey in new[] { "TotalRecordCount", "totalRecordCount" }) if (obj.ContainsKey(countKey)) obj[countKey] = 0;
        }
        else if (node is JsonArray array)
            for (var i = array.Count - 1; i >= 0; i--) if (!Prune(array[i], allowed)) array.RemoveAt(i);
        return true;
    }
}
public record AccessSnapshot(HashSet<Guid> Playable, HashSet<Guid> Navigation);
