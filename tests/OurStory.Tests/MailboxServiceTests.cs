using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OurStory.Core;
using OurStory.Core.Entities;
using OurStory.Data;
using OurStory.Services.Mailbox;
using Xunit;

namespace OurStory.Tests;

public sealed class MailboxServiceTests {
    [Fact]
    public async Task BothPartnersCanReadAndReplyButOnlySenderCanDelete() {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OurStoryDbContext>().UseSqlite(connection).Options;
        await using var db = new OurStoryDbContext(options);
        await db.Database.MigrateAsync();
        var relationship = new CoupleRelationship();
        var other = new CoupleRelationship();
        var boy = new User { UserName = "boy", Role = UserRole.Boy, CoupleRelationship = relationship };
        var girl = new User { UserName = "girl", Role = UserRole.Girl, CoupleRelationship = relationship };
        var outsider = new User { UserName = "outsider", Role = UserRole.Boy, CoupleRelationship = other };
        db.Users.AddRange(boy, girl, outsider);
        await db.SaveChangesAsync();
        var service = new MailboxService(db);
        Assert.Null(await service.SendAsync(boy.Id, "  只给你\n<script>alert(1)</script>  "));
        Assert.Single(await service.ConversationAsync(boy.Id));
        Assert.Empty(await service.ConversationAsync(outsider.Id));
        Assert.Empty(await service.ConversationAsync(0));
        await using var fresh = new OurStoryDbContext(options);
        var reloaded = new MailboxService(fresh);
        var letter = Assert.Single(await reloaded.ConversationAsync(girl.Id));
        Assert.Equal("只给你\n<script>alert(1)</script>", letter.Content);
        Assert.False(await service.DeleteAsync(girl.Id, letter.Id));
        Assert.False(await service.DeleteAsync(outsider.Id, letter.Id));
        Assert.False(await service.DeleteAsync(0, letter.Id));
        Assert.Null(await reloaded.SendAsync(girl.Id, "我也想你"));
        var boyView = await service.ConversationAsync(boy.Id);
        var girlView = await reloaded.ConversationAsync(girl.Id);
        Assert.Equal(2, boyView.Count);
        Assert.Equal(boyView.Select(x => x.Id), girlView.Select(x => x.Id));
        Assert.Equal("我也想你", boyView[0].Content);
        Assert.False(await service.DeleteAsync(boy.Id, boyView[0].Id));
        Assert.True(await service.DeleteAsync(boy.Id, letter.Id));
        Assert.Single(await reloaded.ConversationAsync(girl.Id));
        Assert.True(await reloaded.DeleteAsync(girl.Id, boyView[0].Id));
        Assert.Empty(await service.ConversationAsync(boy.Id));
    }

    [Fact]
    public async Task InvalidMessagesAndInactiveOrAmbiguousRelationshipsAreRejected() {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new OurStoryDbContext(new DbContextOptionsBuilder<OurStoryDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        var relationship = new CoupleRelationship();
        var boy = new User { UserName = "boy", CoupleRelationship = relationship };
        var girl = new User { UserName = "girl", Role = UserRole.Girl, CoupleRelationship = relationship };
        db.Users.AddRange(boy, girl);
        await db.SaveChangesAsync();
        var service = new MailboxService(db);
        Assert.NotNull(await service.SendAsync(0, "no"));
        Assert.NotNull(await service.SendAsync(boy.Id, "   "));
        Assert.NotNull(await service.SendAsync(boy.Id, new string('x', 2001)));
        Assert.Null(await service.SendAsync(boy.Id, new string('x', 2000)));
        var letter = Assert.Single(await service.ConversationAsync(girl.Id));
        girl.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty(await service.ConversationAsync(girl.Id));
        Assert.False(await service.DeleteAsync(girl.Id, letter.Id));
        Assert.NotNull(await service.SendAsync(boy.Id, "no"));
        girl.IsActive = true;
        relationship.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await service.CanAccessAsync(boy.Id));
        Assert.Empty(await service.ConversationAsync(girl.Id));
        Assert.NotNull(await service.SendAsync(boy.Id, "no"));
        relationship.IsActive = true;
        db.Users.Add(new User { UserName = "extra", CoupleRelationship = relationship });
        await db.SaveChangesAsync();
        Assert.NotNull(await service.SendAsync(boy.Id, "no"));
        var extra = await db.Users.SingleAsync(x => x.UserName == "extra");
        Assert.Empty(await service.ConversationAsync(extra.Id));
        Assert.False(await service.DeleteAsync(extra.Id, letter.Id));
        boy.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty(await service.ConversationAsync(boy.Id));
        Assert.False(await service.DeleteAsync(boy.Id, letter.Id));
        boy.IsActive = true;
        girl.CoupleRelationship = new CoupleRelationship();
        await db.SaveChangesAsync();
        Assert.Empty(await service.ConversationAsync(girl.Id));
        Assert.False(await service.DeleteAsync(girl.Id, letter.Id));
        Assert.Equal(1, await db.Set<PrivateMessage>().CountAsync());
    }

    [Fact]
    public async Task ConversationIsNewestFirstAndPaged() {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new OurStoryDbContext(new DbContextOptionsBuilder<OurStoryDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        var relationship = new CoupleRelationship();
        var boy = new User { UserName = "boy", CoupleRelationship = relationship };
        var girl = new User { UserName = "girl", Role = UserRole.Girl, CoupleRelationship = relationship };
        db.Users.AddRange(boy, girl);
        await db.SaveChangesAsync();
        var service = new MailboxService(db);
        for (var i = 0; i < 22; i++) Assert.Null(await service.SendAsync(boy.Id, $"letter {i}"));
        var first = await service.ConversationAsync(girl.Id);
        Assert.Equal(21, first.Count); // One look-ahead row for the next-page link.
        Assert.Equal("letter 21", first[0].Content);
        var second = await service.ConversationAsync(girl.Id, 2);
        Assert.Equal(2, second.Count);
        Assert.Equal("letter 1", second[0].Content);
    }
}
