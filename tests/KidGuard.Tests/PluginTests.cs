using System.Net;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.KidGuard.Providers;
using Jellyfin.Plugin.KidGuard.Services;
using KidGuard.Core;
using MediaBrowser.Model.Users;
using Xunit;

namespace KidGuard.Tests;
public class PluginTests
{
    private static LibraryItem Item(string[]? tags=null, MediaKind kind=MediaKind.Movie) => new(Guid.NewGuid(),"Synthetic",kind,null,"G",2024,[],tags??[],"A scary synopsis is not evidence",new(){{"Tmdb","123"}},[]);
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    { public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) { Calls++;return Task.FromResult(response(request)); } }
    [Fact] public async Task LocalProviderNeverFabricatesSynopsisAdvisories()
    { var a=await new JellyfinMetadataProvider().Get(Item(),new(),default);Assert.Empty(a!.Dimensions); }
    [Fact] public async Task ExplicitStructuredTagsProduceOnlyKnownDimensions()
    { var a=await new JellyfinMetadataProvider().Get(Item(["Advisory:Fear:2","Advisory:Violence:9","Advisory:Nudity:0","Advisory:999:1"]),new(),default);Assert.Equal(2,a!.Dimensions[Dimension.Fear]);Assert.Equal(0,a.Dimensions[Dimension.Nudity]);Assert.Equal(2,a.Dimensions.Count); }
    [Fact] public async Task ExternalProviderUsesOnlyAnIdAndNoProfileInformation()
    {
        var handler=new Handler(r=> { Assert.Equal("https://api.themoviedb.org/3/movie/123/release_dates",r.RequestUri!.ToString());Assert.Null(r.Content);return new(HttpStatusCode.OK){Content=new StringContent("{\"results\":[{\"iso_3166_1\":\"US\",\"release_dates\":[{\"certification\":\"PG\"}]}]}")}; });
        using var p=new TmdbProvider(handler);var a=await p.Get(Item(),new(){ExternalEnabled=true,TmdbToken="fake-test-token"},default);Assert.Equal("PG",a!.Rating);Assert.Empty(a.Dimensions);
    }
    [Fact] public async Task ProviderDisabledDoesNotMakeRequests()
    {var h=new Handler(_=>throw new Exception());using var p=new TmdbProvider(h);Assert.Null(await p.Get(Item(),new(),default));Assert.Equal(0,h.Calls);}
    [Fact] public async Task EpisodeDoesNotBorrowSeriesCertification()
    {var h=new Handler(_=>throw new Exception());using var p=new TmdbProvider(h);Assert.Null(await p.Get(Item(kind:MediaKind.Episode),new(){ExternalEnabled=true,TmdbToken="fake"},default));Assert.Equal(0,h.Calls);}
    [Fact] public async Task ProviderFailureIsExplicit()
    {var h=new Handler(_=>new(HttpStatusCode.Unauthorized));using var p=new TmdbProvider(h);Assert.True((await p.Get(Item(),new(){ExternalEnabled=true,TmdbToken="fake"},default))!.Failed);}
    [Fact] public async Task EmptyCertificationIsNotSafe()
    {var h=new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("{\"results\":[]}")});using var p=new TmdbProvider(h);Assert.True((await p.Get(Item(),new(){ExternalEnabled=true,TmdbToken="fake"},default))!.Failed);}
    [Fact] public async Task CacheHitAvoidsProviderAndKeepsFreshDisplayMetadata()
    { using var a=new ContentAnalysis();var item=Item();var settings=new Settings();var first=await a.Analyze(item,settings,null,default);var second=await a.Analyze(item with{Title="Renamed"},settings,first,default);Assert.Equal(first.RetrievedAt,second.RetrievedAt);Assert.Equal("Renamed",second.Item.Title); }
    [Fact] public void OwnTagsDoNotInvalidateAnalysisCache()
    {var a=Item();Assert.Equal(ContentAnalysis.Fingerprint(a,new()),ContentAnalysis.Fingerprint(a with{Tags=["KidGuard:opaque","KidSafe"]},new()));Assert.NotEqual(ContentAnalysis.Fingerprint(a,new()),ContentAnalysis.Fingerprint(a with{Tags=["Advisory:Fear:3"]},new()));}
    [Fact] public void ChildPolicyDoesNotMutatePreviousPolicy()
    {var original=new UserPolicy(){EnableRemoteAccess=true,AllowedTags=["old"],BlockedTags=["private"],EnabledFolders=[Guid.NewGuid()],EnableAllFolders=false};var next=LibraryAdapter.ChildPolicy(original,new(){AllowDownloads=false,AllowTranscoding=true},"new");Assert.Equal(["old"],original.AllowedTags);Assert.Equal(["new"],next.AllowedTags);Assert.Empty(next.BlockedTags);Assert.Equal(["private"],original.BlockedTags);Assert.False(next.EnableContentDownloading);Assert.False(next.EnableLiveTvAccess);Assert.True(next.EnableRemoteAccess);Assert.True(next.EnableVideoPlaybackTranscoding);Assert.False(next.EnableAllFolders);Assert.Equal(original.EnabledFolders,next.EnabledFolders);}
    [Fact] public void JsonResultsPruneNestedCollectionsAndSearchHints()
    {var allow=Guid.NewGuid();var block=Guid.NewGuid();var node=JsonNode.Parse($$"""{"Items":[{"Id":"{{allow}}"},{"Id":"{{block}}"}],"SearchHints":[{"ItemId":"{{block}}"}],"TotalRecordCount":2}""");Assert.True(SnapshotGuard.Prune(node,id=>id!=block));Assert.Single(node!["Items"]!.AsArray());Assert.Empty(node["SearchHints"]!.AsArray());}
    [Fact] public void JournalRoundTripsFullNativePolicyAndPreviousProfile()
    {var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());try{var store=new AtomicStore<DurableState>(Path.Combine(dir,"state.json"));var journal=new Journal(){Pending=true,BeforePolicy=new(){AllowedTags=["original"],MaxParentalRating=13},Before=new(){Age=9,Applied=true,Approved=[Guid.NewGuid()]}};store.Write(new(){Pending=journal});var restored=store.Read().Pending!;Assert.True(restored.Pending);Assert.Equal(13,restored.BeforePolicy.MaxParentalRating);Assert.Equal(journal.Before.Approved,restored.Before.Approved);}finally{Directory.Delete(dir,true);}}
}
