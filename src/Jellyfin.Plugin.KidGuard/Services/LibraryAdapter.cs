using KidGuard.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.KidGuard.Services;
public sealed class LibraryAdapter(ILibraryManager library, IUserManager users)
{
    public IEnumerable<LibraryItem> Scan(CancellationToken cancellation)
    {
        var start = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var items = library.GetItemList(new InternalItemsQuery { Recursive = true, IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode], StartIndex = start, Limit = 250 });
            foreach (var item in items) yield return Describe(item);
            if (items.Count < 250) yield break;
            start += items.Count;
        }
    }
    public static LibraryItem Describe(BaseItem item) => new(item.Id, item.Name,
        item switch { Movie => MediaKind.Movie, Series => MediaKind.Series, Season => MediaKind.Season, Episode => MediaKind.Episode, _ => throw new ArgumentException("Unsupported media") },
        item.ParentId == Guid.Empty ? null : item.ParentId, item.CustomRating ?? item.OfficialRating, item.ProductionYear,
        item.Genres, item.Tags, item.Overview, new(item.ProviderIds, StringComparer.OrdinalIgnoreCase), item.GetParents().Select(p => p.Id).ToArray());
    public BaseItem? Get(Guid id) => library.GetItemById(id);
    public bool UserExists(Guid id) => users.GetUserById(id) is not null;
    public object[] Users() => users.GetUsers().Select(u => new { u.Id, Name = u.Username, IsAdministrator = users.GetUserDto(u).Policy.IsAdministrator }).Cast<object>().ToArray();
    public UserPolicy Policy(Guid id) => Json.Clone(users.GetUserDto(users.GetUserById(id) ?? throw new ArgumentException("User not found")).Policy);
    public Task SetPolicy(Guid id, UserPolicy policy) => users.UpdatePolicyAsync(id, policy);
    public async Task<Guid> Create(string username, string password)
    {
        if (password.Length < 8) throw new ArgumentException("Use a password of at least 8 characters for the new account.");
        if (users.GetUserByName(username) is not null) throw new ArgumentException("That username already exists.");
        var temporaryName = "kidguard-pending-" + Guid.NewGuid().ToString("N");
        var user = await users.CreateUserAsync(temporaryName).ConfigureAwait(false);
        var policy = Policy(user.Id); policy.IsDisabled = true; policy.IsAdministrator = false;
        await SetPolicy(user.Id, policy).ConfigureAwait(false);
        await users.ChangePassword(user.Id, password).ConfigureAwait(false);
        await users.RenameUser(user.Id, temporaryName, username).ConfigureAwait(false);
        return user.Id;
    }
    public async Task Tag(Guid id, string? remove, string? add, CancellationToken cancellation)
    {
        var item = Get(id); if (item is null) return;
        var updated = ApprovalPlanner.UpdateTags(item.Tags, remove, add);
        if (item.Tags.SequenceEqual(updated)) return;
        var before = item.Tags;
        item.Tags = updated;
        try { await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellation).ConfigureAwait(false); }
        catch
        {
            // A failed write must not look complete on retry through Jellyfin's in-memory item cache.
            if (ReferenceEquals(item.Tags, updated)) item.Tags = before;
            throw;
        }
    }
    public static UserPolicy ChildPolicy(UserPolicy previous, ChildProfile profile, string tag)
    {
        var policy = Json.Clone(previous);
        policy.IsAdministrator = false; policy.IsDisabled = false;
        policy.IsHidden = true; policy.AllowedTags = [tag]; policy.BlockedTags = [];
        // Approved snapshots are the ceiling; a rating cap would contradict Always Allow.
        policy.MaxParentalRating = null; policy.MaxParentalSubRating = null; policy.BlockUnratedItems = [];
        policy.EnableContentDeletion = false; policy.EnableContentDeletionFromFolders = [];
        policy.EnableContentDownloading = profile.AllowDownloads;
        policy.EnableRemoteAccess = profile.AllowRemoteAccess;
        policy.EnableAudioPlaybackTranscoding = profile.AllowTranscoding;
        policy.EnableVideoPlaybackTranscoding = profile.AllowTranscoding;
        policy.EnablePlaybackRemuxing = true;
        policy.EnableCollectionManagement = false; policy.EnableSubtitleManagement = false; policy.EnableLyricManagement = false;
        policy.EnableRemoteControlOfOtherUsers = false; policy.EnableSharedDeviceControl = false;
        policy.EnableLiveTvAccess = false; policy.EnableLiveTvManagement = false;
        policy.EnableAllChannels = false; policy.EnabledChannels = [];
        policy.EnablePublicSharing = false; policy.EnableMediaConversion = false;
        policy.SyncPlayAccess = SyncPlayUserAccessType.None;
        if (profile.EnabledFolders is not null) { policy.EnableAllFolders = false; policy.EnabledFolders = profile.EnabledFolders; }
        return policy;
    }
}
