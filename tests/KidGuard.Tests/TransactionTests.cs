using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.KidGuard.Providers;
using Jellyfin.Plugin.KidGuard.Services;
using KidGuard.Core;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KidGuard.Tests;
public sealed class TransactionTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"kidguard-test-"+Guid.NewGuid());
    private readonly Mock<ILibraryManager> _library=new();
    private readonly Mock<IUserManager> _users=new();
    private readonly Mock<IApplicationPaths> _paths=new();
    private readonly List<BaseItem> _items=[];
    private readonly ContentAnalysis _analysis=new();
    private readonly User _user=new("Fixture child","auth","reset");
    private UserPolicy _policy=new(){AllowedTags=["original"],AuthenticationProviderId="auth",PasswordResetProviderId="reset"};
    private readonly Manager _manager;
    private bool _failTag;
    public TransactionTests()
    {
        _paths.SetupGet(p=>p.DataPath).Returns(_dir);
        _users.Setup(u=>u.GetUserById(_user.Id)).Returns(_user);
        _users.Setup(u=>u.GetUserDto(It.IsAny<User>(),It.IsAny<string?>())).Returns(()=>new UserDto{Id=_user.Id,Policy=Json.Clone(_policy)});
        _users.Setup(u=>u.UpdatePolicyAsync(_user.Id,It.IsAny<UserPolicy>())).Callback<Guid,UserPolicy>((_,p)=>_policy=Json.Clone(p)).Returns(Task.CompletedTask);
        _library.Setup(l=>l.GetItemList(It.IsAny<InternalItemsQuery>())).Returns((InternalItemsQuery q)=>_items.Skip(q.StartIndex??0).Take(q.Limit??250).ToList());
        _library.Setup(l=>l.GetItemById(It.IsAny<Guid>())).Returns((Guid id)=>_items.FirstOrDefault(i=>i.Id==id));
        _library.Setup(l=>l.UpdateItemAsync(It.IsAny<BaseItem>(),It.IsAny<BaseItem>(),It.IsAny<ItemUpdateType>(),It.IsAny<CancellationToken>()))
            .Returns(()=>_failTag?Task.FromException(new IOException("Injected metadata failure")):Task.CompletedTask);
        _manager=new(_paths.Object,new(_library.Object,_users.Object),_analysis,NullLogger<Manager>.Instance);
    }
    private Movie Add(bool detailed=false)
    {var m=new Movie{Id=Guid.NewGuid(),Name="Synthetic",OfficialRating="G",Tags=detailed?Enum.GetValues<Dimension>().Select(d=>$"Advisory:{d}:0").ToArray():[]};_items.Add(m);return m;}
    private async Task Scan(){_manager.StartScan();for(var i=0;i<500&&_manager.Progress.Running;i++)await Task.Delay(10);Assert.False(_manager.Progress.Running);Assert.Null(_manager.Progress.Error);}
    private ChildProfile Profile()=>_manager.Snapshot().Profiles.Single();
    private async Task Create(NewMediaBehavior behavior=NewMediaBehavior.Review)
    {await _manager.SaveProfile(new(){UserId=_user.Id,Label="Fixture",Age=9,NewMedia=behavior});await Scan();}
    private Task Apply()=>_manager.Apply(Profile().Id,Profile().Revision,null,default);
    [Fact] public async Task DraftNeverTouchesNativePolicy()
    {Add();await Create();Assert.Equal(["original"],_policy.AllowedTags);Assert.False(Profile().Applied);}
    [Fact] public async Task FailedApplyIsDurableAndDisabledThenRecoverable()
    {var item=Add();await Create();_failTag=true;await Assert.ThrowsAsync<IOException>(Apply);Assert.True(_manager.RecoveryRequired);Assert.True(_policy.IsDisabled);var restarted=new Manager(_paths.Object,new(_library.Object,_users.Object),_analysis,NullLogger<Manager>.Instance);await restarted.RecoverOnStart();Assert.True(restarted.RecoveryRequired);_failTag=false;await restarted.Undo(Profile().Id,default);Assert.False(restarted.RecoveryRequired);Assert.Equal(["original"],_policy.AllowedTags);Assert.False(_policy.IsDisabled);Assert.DoesNotContain(item.Tags,t=>t.StartsWith("KidGuard:"));}
    [Fact] public async Task NativeApplyAndUndoRestoreOnlyOwnedTags()
    {var item=Add();item.Tags=["Favorite"];await Create();await Apply();Assert.Contains(item.Id,Profile().Approved);Assert.Contains("Favorite",item.Tags);await _manager.Undo(Profile().Id,default);Assert.Equal(["Favorite"],item.Tags);Assert.Equal(["original"],_policy.AllowedTags);}
    [Fact] public async Task AutomaticApprovalUsesApprovedSettingsAndNeverAppliesDraftPermissions()
    {Add(true);await Create(NewMediaBehavior.Automatic);await Apply();var draft=Profile();draft.AllowDownloads=true;draft.Age=17;await _manager.SaveProfile(draft);var fresh=Add(true);await Scan();Assert.Contains(fresh.Id,Profile().Approved);Assert.False(_policy.EnableContentDownloading);}
    [Fact] public async Task OptingInOnlyInDraftDoesNotAutoApprove()
    {Add(true);await Create();await Apply();var draft=Profile();draft.NewMedia=NewMediaBehavior.Automatic;await _manager.SaveProfile(draft);var fresh=Add(true);await Scan();Assert.DoesNotContain(fresh.Id,Profile().Approved);}
    [Fact] public async Task UnknownNewItemsStayExcludedEvenInAutomaticMode()
    {Add(true);await Create(NewMediaBehavior.Automatic);await Apply();var fresh=Add();fresh.OfficialRating=null;await Scan();Assert.DoesNotContain(fresh.Id,Profile().Approved);}
    [Fact] public async Task OrdinaryDecisionsExpireOnAnalysisButAlwaysDecisionsRemain()
    {var one=Add();var two=Add();await Create();await _manager.Overrides(Profile().Id,[one.Id],OverrideKind.Block,Profile().Revision);await _manager.Overrides(Profile().Id,[two.Id],OverrideKind.AlwaysBlock,Profile().Revision);await Scan();Assert.False(Profile().Overrides.ContainsKey(one.Id));Assert.Equal(OverrideKind.AlwaysBlock,Profile().Overrides[two.Id]);}
    private void DeleteFixtureUser() => _users.Setup(u => u.GetUserById(_user.Id)).Returns((User?)null);
    private DurableState Stored() => new AtomicStore<DurableState>(Path.Combine(_dir, "kidguard", "state.json")).Read();
    [Fact] public async Task DeletedUserRemovesProfileSnapshotsAndOnlyOwnedTags()
    {
        var item = Add(); item.Tags = ["Favorite"]; await Create(); await Apply(); var id = Profile().Id;
        DeleteFixtureUser(); await _manager.ReconcileDeletedUsers();
        Assert.Empty(_manager.Snapshot().Profiles); Assert.Null(_manager.AccessFor(_user.Id));
        Assert.Empty(_manager.FamilyIds(_manager.Snapshot())); Assert.Equal(["Favorite"], item.Tags);
        var stored = Stored(); Assert.Empty(stored.Undo); Assert.Empty(stored.ApprovedSettings); Assert.Empty(stored.OrphanTags);
        Assert.Contains(stored.Data.Audit, a => a.ProfileId == id && a.Action.Contains("user was deleted"));
        await _manager.ReconcileDeletedUsers(); Assert.Single(Stored().Data.Audit, a => a.Action.Contains("user was deleted"));
        _users.Verify(u => u.CreateUserAsync(It.IsAny<string>()), Times.Never);
        _users.Verify(u => u.DeleteUserAsync(It.IsAny<Guid>()), Times.Never);
    }
    [Fact] public async Task ReconciliationPreservesUncreatedDraftsAndExistingUsers()
    {
        Add(); await Create(); var live = Profile().Id;
        var draft = await _manager.SaveProfile(new() { Label = "Uncreated", NewUsername = "Future child", Age = 6 });
        await _manager.ReconcileDeletedUsers(); Assert.Equal(2, _manager.Snapshot().Profiles.Count);
        DeleteFixtureUser(); await _manager.ReconcileDeletedUsers();
        Assert.Equal(draft.Id, _manager.Snapshot().Profiles.Single().Id);
        Assert.DoesNotContain(_manager.Snapshot().Profiles, p => p.Id == live);
        Assert.Null(_manager.Snapshot().Profiles.Single().UserId);
    }
    [Fact] public async Task DeletedAccountDuringInterruptedApplyDoesNotBlockStartupRecovery()
    {
        Add(); await Create(); _failTag = true; await Assert.ThrowsAsync<IOException>(Apply);
        Assert.True(_manager.RecoveryRequired); DeleteFixtureUser(); _failTag = false;
        var restarted = new Manager(_paths.Object, new(_library.Object, _users.Object), _analysis, NullLogger<Manager>.Instance);
        await restarted.RecoverOnStart(); Assert.False(restarted.RecoveryRequired); Assert.Empty(restarted.Snapshot().Profiles);
        Assert.Null(Stored().Pending);
    }
    [Fact] public async Task LookupFailureNeverMeansAccountDeletion()
    {
        Add(); await Create(); var id = Profile().Id;
        _users.Setup(u => u.GetUserById(_user.Id)).Throws(new IOException("User database unavailable"));
        await Assert.ThrowsAsync<IOException>(() => _manager.ReconcileDeletedUsers());
        Assert.Equal(id, Profile().Id); Assert.Single(Stored().Data.Profiles);
    }
    [Fact] public async Task OrphanMetadataCleanupIsDurableAndRetriedAfterFailure()
    {
        var item = Add(); item.Tags = ["Favorite"]; await Create(); await Apply();
        DeleteFixtureUser(); _failTag = true; await _manager.ReconcileDeletedUsers();
        Assert.Empty(_manager.Snapshot().Profiles); Assert.NotEmpty(Stored().OrphanTags);
        Assert.Contains(item.Tags, t => t.StartsWith("KidGuard:"));
        _failTag = false;
        var restarted = new Manager(_paths.Object, new(_library.Object, _users.Object), _analysis, NullLogger<Manager>.Instance);
        await restarted.ReconcileDeletedUsers(); Assert.Empty(Stored().OrphanTags);
        Assert.Equal(["Favorite"], item.Tags); Assert.False(Stored().RefreshFamilyPending);
    }
    [Fact] public async Task SameUsernameWithDifferentIdDoesNotInheritDeletedProfile()
    {
        Add(); await Create(); await Apply(); DeleteFixtureUser();
        var replacement = new User(_user.Username, "auth", "reset");
        _users.Setup(u => u.GetUserById(replacement.Id)).Returns(replacement);
        await _manager.ReconcileDeletedUsers(); Assert.Empty(_manager.Snapshot().Profiles);
        Assert.Null(_manager.AccessFor(replacement.Id));
    }
    public void Dispose(){_analysis.Dispose();if(Directory.Exists(_dir))Directory.Delete(_dir,true);}
}
