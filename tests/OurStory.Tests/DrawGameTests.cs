using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OurStory.Core;
using OurStory.Core.Entities;
using OurStory.Data;
using OurStory.Services.Games.Draw;
using Xunit;

namespace OurStory.Tests;

public sealed class DrawGameTests : IAsyncLifetime, IAsyncDisposable {
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly DrawCoordinator coordinator = new();
    private readonly FakeTimeProvider time = new();
    private OurStoryDbContext db = null!;
    private DrawWordService words = null!;
    private DrawGameService service = null!;
    private User boy = null!, girl = null!, stranger = null!;
    private OurStoryDbContext Open() => new(new DbContextOptionsBuilder<OurStoryDbContext>().UseSqlite(connection).Options);
    private DrawGameService GameService(OurStoryDbContext context) {
        var access = new DrawPairAccess(context);
        return new(context, access, new(context, access, coordinator), coordinator, time);
    }
    public async Task InitializeAsync() {
        await connection.OpenAsync(); db = Open(); await db.Database.MigrateAsync();
        var couple = new CoupleRelationship(); var other = new CoupleRelationship();
        boy = new() { UserName = "draw-boy", Role = UserRole.Boy, CoupleRelationship = couple };
        girl = new() { UserName = "draw-girl", Role = UserRole.Girl, CoupleRelationship = couple };
        stranger = new() { UserName = "stranger", CoupleRelationship = other };
        db.Users.AddRange(boy, girl, stranger, new User { UserName = "stranger-partner", Role = UserRole.Girl, CoupleRelationship = other });
        await db.SaveChangesAsync();
        var access = new DrawPairAccess(db); words = new(db, access, coordinator); service = GameService(db);
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); coordinator.Dispose(); }
    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());
    private static DrawCommand Command(string action, DrawView? game = null) => new() {
        Action = action, RequestId = Guid.NewGuid().ToString(), RoundId = game?.RoundId ?? "", CanvasEpoch = game?.CanvasEpoch ?? "", CanvasSince = game?.StrokeCount ?? 0
    };
    private async Task<DrawView> Start() {
        var response = await service.ActAsync(boy.Id, Command("start"));
        Assert.True(response.Ok, response.Message); return response.Game!;
    }
    private async Task<DrawView> Choose(int userId, DrawView game, int choice = 0) {
        var command = Command("choose", game); command.Choice = choice;
        var result = await service.ActAsync(userId, command); Assert.True(result.Ok, result.Message); return result.Game!;
    }
    private static DrawCommand Ink(DrawView game, params DrawPoint[] points) {
        var command = Command("ink", game); command.Stroke = new(command.RequestId, "#30303b", 8, false, points); return command;
    }

    [Fact]
    public void BuiltInVocabularyHasThreeHundredDistinctWordsAndValidMetadata() {
        Assert.Equal(300, DrawWordService.BuiltIns.Count);
        Assert.Equal(300, DrawWordService.BuiltIns.Select(x => x.Key).Distinct().Count());
        Assert.Equal(300, DrawWordService.BuiltIns.Select(x => x.Answer).Distinct().Count());
        Assert.Equal(6, DrawWordService.BuiltIns.Select(x => x.Category).Distinct().Count());
        Assert.All(DrawWordService.BuiltIns, word => { Assert.InRange(word.Difficulty, 1, 3); Assert.NotEmpty(word.Answer); });
    }

    [Fact]
    public async Task GuesserNeverReceivesCandidatesAnswersOrAliasesBeforeReveal() {
        Assert.Null(await words.SaveAsync(boy.Id, 0, new("星光小屋", "Home\n我们的家", "专属回忆", 2, true)));
        var start = await Start();
        Assert.Equal(3, start.Choices.Length); Assert.Equal("星光小屋", start.Choices[0].Answer);
        var other = (await service.GetAsync(girl.Id)).Game!;
        Assert.Empty(other.Choices); Assert.Null(other.Answer); Assert.Null(other.HintCategory);
        var chosen = await Choose(boy.Id, start);
        Assert.Equal("星光小屋", chosen.Answer);
        other = (await service.GetAsync(girl.Id)).Game!;
        var json = JsonSerializer.Serialize(other, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.DoesNotContain("星光小屋", json); Assert.DoesNotContain("我们的家", json); Assert.DoesNotContain("Home", json);
        Assert.Empty(other.Choices); Assert.Null(other.Answer);
        var guess = Command("guess", other); guess.Text = " ＨＯＭＥ ";
        var result = await service.ActAsync(girl.Id, guess);
        Assert.True(result.Ok); Assert.Equal("reveal", result.Game!.Stage); Assert.Equal(1, result.Game.Score);
        Assert.Equal("星光小屋", result.Game.Answer);
        var retried = await service.ActAsync(girl.Id, guess);
        Assert.Equal(1, retried.Game!.Score); Assert.Single(retried.Game.Guesses);
    }

    [Fact]
    public async Task ServerClockControlsHintAndRejectsGuessAtDeadline() {
        var chosen = await Choose(boy.Id, await Start());
        time.Advance(TimeSpan.FromSeconds(44));
        var state = (await service.GetAsync(girl.Id)).Game!; Assert.Null(state.HintCategory);
        time.Advance(TimeSpan.FromSeconds(1));
        state = (await service.GetAsync(girl.Id)).Game!;
        Assert.NotNull(state.HintCategory); Assert.NotNull(state.HintLength); Assert.Null(state.Answer);
        time.Advance(TimeSpan.FromSeconds(45));
        var guess = Command("guess", state); guess.Text = chosen.Answer!;
        var result = await service.ActAsync(girl.Id, guess);
        Assert.False(result.Ok); Assert.Equal("reveal", result.Game!.Stage); Assert.Equal(0, result.Game.Score);
        Assert.Empty(result.Game.Guesses); Assert.Equal(chosen.Answer, result.Game.Answer);
        Assert.Single((await service.GetAsync(boy.Id)).Game!.Results);
    }

    [Fact]
    public async Task SixRoundsAlternateRolesAvoidRepeatWordsAndFinishWithSharedScore() {
        var state = await Start(); var used = new HashSet<string>();
        for (var round = 1; round <= 6; round++) {
            var drawer = round % 2 == 1 ? boy.Id : girl.Id;
            var guesser = drawer == boy.Id ? girl.Id : boy.Id;
            state = (await service.GetAsync(drawer)).Game!;
            Assert.True(state.IsDrawer); Assert.Equal(round, state.Round); Assert.Equal(3, state.Choices.Length);
            state = await Choose(drawer, state); Assert.True(used.Add(state.Answer!));
            var guess = Command("guess", state); guess.Text = state.Answer!;
            state = (await service.ActAsync(guesser, guess)).Game!;
            Assert.Equal(round, state.Score);
            state = (await service.ActAsync(drawer, Command("next", state))).Game!;
        }
        Assert.Equal("finished", state.Stage); Assert.Equal(6, state.Results.Length); Assert.Equal(6, state.Score);
        var rematch = await service.ActAsync(boy.Id, Command("start", state));
        Assert.True(rematch.Ok); Assert.False(rematch.Game!.IsDrawer); Assert.Equal(0, rematch.Game.Score);
    }

    [Fact]
    public async Task StrokePermissionsIncrementalSyncDeduplicationAndClearAreEnforced() {
        var start = await Start();
        Assert.False((await service.ActAsync(girl.Id, new DrawCommand { Action = "choose", RequestId = Guid.NewGuid().ToString(), RoundId = start.RoundId, Choice = 0 })).Ok);
        var state = await Choose(boy.Id, start);
        var stroke = Ink(state, new DrawPoint(10, 10), new(200, 200));
        Assert.False((await service.ActAsync(girl.Id, stroke)).Ok);
        Assert.True((await service.ActAsync(boy.Id, stroke)).Ok);
        Assert.True((await service.ActAsync(boy.Id, stroke)).Ok);
        Assert.Equal(1, (await service.GetAsync(girl.Id)).Game!.StrokeCount);
        Assert.False((await service.ActAsync(boy.Id, Ink(state, new DrawPoint(-1, 0)))).Ok);
        Assert.False((await service.ActAsync(boy.Id, Ink(state, new DrawPoint(0, 701)))).Ok);
        Assert.False((await service.ActAsync(boy.Id, Ink(state, new DrawPoint(double.NaN, 5)))).Ok);
        var second = await service.ActAsync(boy.Id, Ink(state, new DrawPoint(300, 300)));
        Assert.True(second.Ok);
        var delta = (await service.GetAsync(girl.Id, state.CanvasEpoch, 1)).Game!;
        Assert.Equal(1, delta.StrokeBase); Assert.Single(delta.Strokes); Assert.Equal(2, delta.StrokeCount);
        var clear = await service.ActAsync(boy.Id, Command("clear", state));
        Assert.True(clear.Ok); Assert.NotEqual(state.CanvasEpoch, clear.Game!.CanvasEpoch);
        Assert.False((await service.ActAsync(boy.Id, Ink(state, new DrawPoint(5, 5)))).Ok);
        Assert.Empty((await service.GetAsync(girl.Id, state.CanvasEpoch, 2)).Game!.Strokes);
    }

    [Fact]
    public async Task CrossRelationshipAccessAndDisabledAccountsAreRejected() {
        var state = await Choose(boy.Id, await Start());
        Assert.True((await service.GetAsync(0)).Forbidden);
        Assert.Null((await service.GetAsync(stranger.Id)).Game);
        Assert.False((await service.ActAsync(stranger.Id, Command("end", state))).Ok);
        var guess = Command("guess", state); guess.Text = state.Answer!;
        Assert.False((await service.ActAsync(boy.Id, guess)).Ok);
        girl.IsActive = false; await db.SaveChangesAsync();
        Assert.True((await service.GetAsync(girl.Id)).Forbidden);
        Assert.True((await service.ActAsync(girl.Id, guess)).Forbidden);
    }

    [Fact]
    public async Task PersistedGameRestoresDrawingAndOldRoundCommandsCannotAffectNewRound() {
        var state = await Choose(boy.Id, await Start());
        var old = Ink(state, new DrawPoint(20, 20));
        await service.ActAsync(boy.Id, old);
        await using var fresh = Open();
        var restored = (await GameService(fresh).GetAsync(girl.Id)).Game!;
        Assert.Equal("drawing", restored.Stage); Assert.Single(restored.Strokes); Assert.Null(restored.Answer);
        var guess = Command("guess", state); guess.Text = state.Answer!;
        var reveal = (await service.ActAsync(girl.Id, guess)).Game!;
        var next = (await service.ActAsync(boy.Id, Command("next", reveal))).Game!;
        old.RequestId = Guid.NewGuid().ToString();
        Assert.False((await service.ActAsync(boy.Id, old)).Ok);
        Assert.Empty(next.Strokes); Assert.Equal(2, next.Round);
    }

    [Fact]
    public async Task WordChangesAreScopedAndDoNotAlterAnAlreadySelectedAnswer() {
        var list = await words.ListAsync(boy.Id, null, 1); Assert.Equal(300, list!.Total);
        var builtIn = list.Items[0];
        Assert.Null(await words.GetAsync(stranger.Id, builtIn.Id));
        Assert.False(await words.RemoveAsync(stranger.Id, builtIn.Id, true));
        Assert.NotNull(await words.SaveAsync(stranger.Id, builtIn.Id, new("越权", "", "其他", 1, true)));
        Assert.True(await words.RemoveAsync(boy.Id, builtIn.Id, true));
        Assert.Equal(299, (await words.ListAsync(boy.Id, null, 1))!.Total);
        Assert.Equal(299, (await words.ListAsync(boy.Id, null, 1))!.Total);
        Assert.Null(await words.SaveAsync(boy.Id, 0, new("专属测试词", "秘密别名", "回忆", 1, true)));
        var state = await Choose(boy.Id, await Start()); Assert.Equal("专属测试词", state.Answer);
        var word = Assert.Single((await words.ListAsync(boy.Id, "专属测试词", 1))!.Items);
        Assert.Null(await words.SaveAsync(boy.Id, word.Id, new("已修改", "新别名", "回忆", 2, false)));
        var guess = Command("guess", state); guess.Text = "秘密别名";
        Assert.Equal(1, (await service.ActAsync(girl.Id, guess)).Game!.Score);
    }

    [Fact]
    public async Task DisabledVocabularyPreventsStartAndInvalidEditsAreRejected() {
        await words.ListAsync(boy.Id, null, 1);
        await db.Set<DrawWord>().Where(x => x.RelationshipId == boy.CoupleRelationshipId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Enabled, false));
        Assert.False((await service.ActAsync(boy.Id, Command("start"))).Ok);
        Assert.Null((await service.GetAsync(boy.Id)).Game);
        Assert.NotNull(await words.SaveAsync(boy.Id, 0, new(" ", "", "回忆", 1, true)));
        Assert.NotNull(await words.SaveAsync(boy.Id, 0, new("测试", "", "回忆", 7, true)));
    }
}
