using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OurStory.Core;
using OurStory.Core.Entities;
using OurStory.Data;
using OurStory.Services.Games;
using Xunit;

namespace OurStory.Tests;

public sealed class GomokuRulesTests {
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    public void FiveInEachDirectionWins(int dr, int dc) {
        var moves = new List<int>();
        for (var i = 0; i < 5; i++) {
            moves.Add((3 + i * dr) * 15 + 7 + i * dc);
            Assert.Equal(i == 4 ? 1 : 0, GomokuRules.Outcome(moves));
            if (i < 4) moves.Add(14 * 15 + i * 2);
        }
    }

    [Fact]
    public void SixInARowAlsoWinsAndRowsDoNotWrap() {
        var moves = new List<int>();
        foreach (var col in new[] { 0, 1, 2, 4, 5 }) {
            moves.Add(col);
            moves.Add(210 + (moves.Count / 2) * 2);
        }
        moves.Add(3);
        Assert.Equal(1, GomokuRules.Outcome(moves));
        Assert.Equal(0, GomokuRules.Outcome(new[] { 13, 210, 14, 212, 15, 214, 16, 216, 17 }));
        Assert.Throws<ArgumentException>(() => GomokuRules.Outcome(new[] { 0, 0 }));
        Assert.Throws<ArgumentException>(() => GomokuRules.Outcome(new[] { 225 }));
    }

    [Fact]
    public void FullBoardWithoutFiveIsDraw() {
        var black = new Queue<int>();
        var white = new Queue<int>();
        for (var point = 0; point < 225; point++) {
            if ((point / 15 + 2 * (point % 15)) % 4 < 2) black.Enqueue(point);
            else white.Enqueue(point);
        }
        // This coloring has no run of five in any of the four directions.
        var moves = new List<int>();
        while (black.Count > 0 || white.Count > 0) {
            moves.Add((moves.Count % 2 == 0 ? black : white).Dequeue());
            Assert.Equal(moves.Count == 225 ? 3 : 0, GomokuRules.Outcome(moves));
        }
    }
}

public sealed class GomokuServiceTests : IAsyncLifetime {
    private readonly string database = Path.Combine(Path.GetTempPath(), $"ourstory-gomoku-{Guid.NewGuid():N}.db");
    private OurStoryDbContext db = null!;
    private GomokuService service = null!;
    private User boy = null!;
    private User girl = null!;
    private User outsider = null!;
    private OurStoryDbContext Open() => new(new DbContextOptionsBuilder<OurStoryDbContext>().UseSqlite($"Data Source={database};Pooling=False").Options);

    public async Task InitializeAsync() {
        db = Open();
        await db.Database.MigrateAsync();
        var relationship = new CoupleRelationship();
        boy = new User { UserName = "boy", Role = UserRole.Boy, CoupleRelationship = relationship };
        girl = new User { UserName = "girl", Role = UserRole.Girl, CoupleRelationship = relationship };
        outsider = new User { UserName = "outsider", CoupleRelationship = new CoupleRelationship() };
        db.Users.AddRange(boy, girl, outsider);
        await db.SaveChangesAsync();
        service = new GomokuService(db);
    }

    public async Task DisposeAsync() {
        await db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(database);
    }

    [Fact]
    public async Task BothPartnersSharePersistentGameAndMustAlternate() {
        var started = await service.ActAsync(boy.Id, "start", 0);
        Assert.True(started.Ok);
        Assert.Equal(1, started.Game!.MyColor);
        var other = await service.GetAsync(girl.Id);
        Assert.Equal(2, other.Game!.MyColor);
        Assert.False((await service.ActAsync(girl.Id, "move", 1, 7, 7)).Ok);
        Assert.False((await service.ActAsync(boy.Id, "move", 1, -1, 7)).Ok);
        Assert.False((await service.ActAsync(boy.Id, "move", 1, 15, 0)).Ok);
        Assert.True((await service.ActAsync(boy.Id, "move", 1, 7, 7)).Ok);
        Assert.False((await service.ActAsync(boy.Id, "move", 2, 7, 8)).Ok);
        Assert.False((await service.ActAsync(girl.Id, "move", 2, 7, 7)).Ok);
        Assert.True((await service.ActAsync(girl.Id, "move", 2, 7, 8)).Ok);
        Assert.False((await service.ActAsync(boy.Id, "start", 3)).Ok);
        await using var fresh = Open();
        var restored = (await new GomokuService(fresh).GetAsync(boy.Id)).Game!;
        Assert.Equal(new[] { 112, 113 }, restored.Moves);
        Assert.Equal(3, restored.Version);
    }

    [Fact]
    public async Task WinnerEndsGameAndRematchSwapsColors() {
        await service.ActAsync(boy.Id, "start", 0);
        var version = 1;
        for (var i = 0; i < 5; i++) {
            Assert.True((await service.ActAsync(boy.Id, "move", version++, 0, i)).Ok);
            if (i < 4) Assert.True((await service.ActAsync(girl.Id, "move", version++, 14, i * 2)).Ok);
        }
        Assert.Equal(1, (await service.GetAsync(girl.Id)).Game!.Outcome);
        Assert.False((await service.ActAsync(girl.Id, "move", version, 5, 5)).Ok);
        var rematch = await service.ActAsync(girl.Id, "start", version);
        Assert.True(rematch.Ok);
        Assert.Equal(1, rematch.Game!.MyColor);
        Assert.Empty(rematch.Game.Moves);
        Assert.True((await service.ActAsync(girl.Id, "resign", rematch.Game.Version)).Ok);
        Assert.Equal(2, (await service.GetAsync(boy.Id)).Game!.Outcome);
    }

    [Fact]
    public async Task UnauthorizedAndInactiveMembersCannotReadOrMutate() {
        await service.ActAsync(boy.Id, "start", 0);
        Assert.False((await service.GetAsync(0)).Ok);
        Assert.False((await service.GetAsync(outsider.Id)).Ok);
        Assert.False((await service.ActAsync(outsider.Id, "resign", 1)).Ok);
        girl.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False((await service.GetAsync(girl.Id)).Ok);
        Assert.False((await service.ActAsync(girl.Id, "move", 1, 0, 0)).Ok);
        girl.IsActive = true;
        boy.CoupleRelationship!.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False((await service.GetAsync(boy.Id)).Ok);
    }

    [Fact]
    public async Task ReplacementPartnerDoesNotGainAccessToPreviousGame() {
        await service.ActAsync(boy.Id, "start", 0);
        var relationship = boy.CoupleRelationship;
        girl.CoupleRelationship = new CoupleRelationship();
        outsider.CoupleRelationship = relationship;
        await db.SaveChangesAsync();
        Assert.False((await service.GetAsync(outsider.Id)).Ok);
        Assert.False((await service.GetAsync(boy.Id)).Ok);
        Assert.False((await service.ActAsync(outsider.Id, "start", 1)).Ok);
    }

    [Fact]
    public async Task SimultaneousStartCreatesExactlyOneGame() {
        await using var one = Open();
        await using var two = Open();
        var results = await Task.WhenAll(
            new GomokuService(one).ActAsync(boy.Id, "start", 0),
            new GomokuService(two).ActAsync(girl.Id, "start", 0));
        Assert.Single(results, x => x.Ok);
        Assert.Equal(1, await db.Set<GomokuGame>().CountAsync());
        Assert.Equal(1, (await service.GetAsync(boy.Id)).Game!.Version);
    }

    [Fact]
    public async Task CompetingRequestsCannotPlaceTwoStonesOrOverwriteGame() {
        await service.ActAsync(boy.Id, "start", 0);
        await using var one = Open();
        await using var two = Open();
        var results = await Task.WhenAll(
            new GomokuService(one).ActAsync(boy.Id, "move", 1, 7, 7),
            new GomokuService(two).ActAsync(boy.Id, "move", 1, 7, 8));
        Assert.Single(results, x => x.Ok);
        var game = (await service.GetAsync(boy.Id)).Game!;
        Assert.Single(game.Moves);
        Assert.Equal(2, game.Version);
        Assert.False((await service.ActAsync(girl.Id, "resign", 1)).Ok);
        Assert.Equal(0, (await service.GetAsync(boy.Id)).Game!.Outcome);
    }
}
