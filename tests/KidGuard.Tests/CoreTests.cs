using KidGuard.Core;
using Xunit;

namespace KidGuard.Tests;
public class CoreTests
{
    private static Assessment Item(string? rating, MediaKind kind = MediaKind.Movie, Guid? id = null, Guid[]? ancestors = null, params Advisory[] extra)
    {
        var item = new LibraryItem(id ?? Guid.NewGuid(), "Synthetic test title", kind, ancestors?.FirstOrDefault(), rating, 2024, [], [], null, [], ancestors ?? []);
        return new(item, [new("Fixture metadata", rating, [], "Synthetic evidence"), .. extra], DateTimeOffset.UtcNow, "test");
    }
    private static Recommendation Evaluate(ChildProfile p, Assessment a) => new RecommendationEngine().Evaluate(p, a, new Dictionary<Guid, Assessment> { [a.Item.Id] = a });
    [Theory]
    [InlineData(12, "PG-13", Decision.Block)]
    [InlineData(13, "PG-13", Decision.Allow)]
    [InlineData(13, "TV-14", Decision.Block)]
    [InlineData(14, "TV-14", Decision.Allow)]
    [InlineData(6, "PG", Decision.Block)]
    [InlineData(6, "G", Decision.Allow)]
    [InlineData(17, "TV-MA", Decision.Block)]
    public void AgeBoundaries(int age, string rating, Decision expected) => Assert.Equal(expected, Evaluate(new() { Age = age, Approach = Approach.Standard }, Item(rating)).Decision);
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("NR")] [InlineData("unknown-12")]
    public void UnknownNeverMeansSafe(string? rating) => Assert.Equal(Decision.Review, Evaluate(new(), Item(rating)).Decision);
    [Fact] public void SourcesDisagree() { var a=Item("G", extra: new Advisory("External", "R", [], "Fixture")); Assert.Equal(Decision.Review,Evaluate(new() { Age=17 },a).Decision); }
    [Fact] public void FailureRemainsReview() { var a=Item("G", extra: new Advisory("External", null, [], "Timeout",true)); Assert.Equal(Decision.Review,Evaluate(new(),a).Decision); }
    [Theory] [InlineData(OverrideKind.AlwaysAllow,Decision.Allow)] [InlineData(OverrideKind.AlwaysBlock,Decision.Block)] [InlineData(OverrideKind.Allow,Decision.Allow)] [InlineData(OverrideKind.Block,Decision.Block)]
    public void ParentOverrideWinsEvenWithMissingOrConflictingData(OverrideKind kind, Decision decision)
    { var a=Item(null,extra:new Advisory("bad",null,[],"fail",true));var p=new ChildProfile();p.Overrides[a.Item.Id]=kind;Assert.Equal(decision,Evaluate(p,a).Decision); }
    [Fact] public void DifferentChildrenAreIndependent() { var a=Item("PG-13");Assert.Equal(Decision.Block,Evaluate(new(){Age=6},a).Decision);Assert.Equal(Decision.Allow,Evaluate(new(){Age=14},a).Decision); }
    [Fact] public void ExplicitUnknownDimensionNeedsReview() => Assert.Equal(Decision.Review,Evaluate(new(){Tolerances = {[Dimension.Fear]=1}},Item("G")).Decision);
    [Theory] [InlineData(Dimension.Violence)] [InlineData(Dimension.Fear)] [InlineData(Dimension.SexualContent)] [InlineData(Dimension.Nudity)] [InlineData(Dimension.Profanity)] [InlineData(Dimension.Substances)] [InlineData(Dimension.MatureThemes)] [InlineData(Dimension.DeathGrief)] [InlineData(Dimension.DisturbingImagery)]
    public void DimensionToleranceIsEnforced(Dimension dimension)
    { var a=Item("G",extra:new Advisory("Structured",null,new(){[dimension]=3},"Explicit metadata"));Assert.Equal(Decision.Block,Evaluate(new(){Tolerances={[dimension]=1}},a).Decision); }
    [Fact] public void CalibrationBoundedAndContradictionsConservative()
    {
        var yes=Item("PG-13");var no=Item("PG");var cache=new Dictionary<Guid,Assessment>{{yes.Item.Id,yes},{no.Item.Id,no}};
        var p=new ChildProfile(){Age=8,Calibration=[new(yes.Item.Id,Decision.Allow)]};Assert.Equal(3,CalibrationEngine.Offset(p,cache));
        p.Calibration.Add(new(no.Item.Id,Decision.Block));Assert.True(CalibrationEngine.Offset(p,cache)<=0);
        p.Calibration=[new(yes.Item.Id,Decision.Review)];Assert.Equal(0,CalibrationEngine.Offset(p,cache));
    }
    [Fact] public void CalibrationSelectsActualTitlesAcrossRatings()
    { var items=new[]{Item("G"),Item("PG"),Item("PG-13"),Item("TV-14"),Item("R"),Item("TV-MA"),Item("NR")};var result=CalibrationEngine.SelectExamples(items);Assert.Equal(6,result.Length);Assert.DoesNotContain(result,x=>x.Rating=="NR"); }
    [Fact] public void SeriesOverrideAppliesOnlyToKnownEpisodesWithSpecificExceptions()
    {
        var series=Item("PG",MediaKind.Series);var season=Item("PG",MediaKind.Season,ancestors:[series.Item.Id]);
        var good=Item(null,MediaKind.Episode,ancestors:[season.Item.Id,series.Item.Id]);var bad=Item(null,MediaKind.Episode,ancestors:[season.Item.Id,series.Item.Id]);
        var cache=new[]{series,season,good,bad}.ToDictionary(a=>a.Item.Id);var p=new ChildProfile();p.Overrides[series.Item.Id]=OverrideKind.AlwaysAllow;p.Overrides[bad.Item.Id]=OverrideKind.AlwaysBlock;
        var plan=ApprovalPlanner.Build(p,cache);Assert.Equal([good.Item.Id],plan.Playable);Assert.Contains(series.Item.Id,plan.Navigation);Assert.Contains(season.Item.Id,plan.Navigation);
        var future=Guid.NewGuid();Assert.DoesNotContain(future,plan.Playable);
    }
    [Fact] public void ClosestSeasonOverrideWins()
    { var series=Guid.NewGuid();var season=Guid.NewGuid();var episode=Item("G",MediaKind.Episode,ancestors:[season,series]);var p=new ChildProfile();p.Overrides[series]=OverrideKind.AlwaysAllow;p.Overrides[season]=OverrideKind.AlwaysBlock;Assert.Empty(ApprovalPlanner.Build(p,new Dictionary<Guid,Assessment>{{episode.Item.Id,episode}}).Playable); }
    [Fact] public void TagEditsPreserveUnrelatedMetadataAndAreIdempotent()
    { var tags=ApprovalPlanner.UpdateTags(["Favorite","old","KidGuard:other"],"old","new");Assert.Equal(["Favorite","KidGuard:other","new"],tags);Assert.Equal(tags,ApprovalPlanner.UpdateTags(tags,"old","new")); }
    [Fact] public void ProfileTagsContainNoPersonalInfoAndAreIndependent()
    { var id=Guid.NewGuid();Assert.NotEqual(ApprovalPlanner.Tag(id,Guid.NewGuid()),ApprovalPlanner.Tag(id,Guid.NewGuid()));Assert.StartsWith("KidGuard:",ApprovalPlanner.Tag(id,Guid.NewGuid())); }
    [Fact] public void AtomicStoreRoundTripsOverridesAndRefusesCorruption()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());try{
            var path=Path.Combine(dir,"state.json");var store=new AtomicStore<State>(path);var p=new ChildProfile();p.Overrides[Guid.NewGuid()]=OverrideKind.AlwaysBlock;var state=new State(){Profiles=[p]};store.Write(state);Assert.Equal(p.Overrides,store.Read().Profiles[0].Overrides);File.WriteAllText(path,"corrupt");Assert.Throws<System.Text.Json.JsonException>(()=>store.Read());
        }finally{Directory.Delete(dir,true);}
    }
    [Fact] public void TenThousandItemsProduceAnExactPlan()
    {var items=Enumerable.Range(0,10000).Select(_=>Item("G")).ToDictionary(a=>a.Item.Id);var p=new ChildProfile();var engine=new RecommendationEngine();foreach(var a in items.Values)p.Recommendations[a.Item.Id]=engine.Evaluate(p,a,items);Assert.Equal(10000,ApprovalPlanner.Build(p,items).Playable.Count);}
}
