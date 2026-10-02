using Microsoft.EntityFrameworkCore;
using OurStory.Core;
using OurStory.Core.Entities;
using OurStory.Data;

namespace OurStory.Services.Mailbox;

/// <summary>身份由服务端提供；收件人只能是当前有效关系中的另一位。</summary>
public sealed class MailboxService(OurStoryDbContext db) {
    public const int PageSize = 20;

    private Task<User?> MemberAsync(int userId, CancellationToken ct) => db.Users.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive
            && (x.Role == UserRole.Boy || x.Role == UserRole.Girl)
            && x.CoupleRelationshipId != null && x.CoupleRelationship!.IsActive, ct);

    public async Task<bool> CanAccessAsync(int userId, CancellationToken ct = default) =>
        await MemberAsync(userId, ct) is not null;

    public async Task<List<PrivateMessage>> ConversationAsync(int userId, int page = 1, CancellationToken ct = default) {
        var member = await MemberAsync(userId, ct);
        if (member is null) return [];
        return await db.Set<PrivateMessage>().AsNoTracking()
            .Where(x => (x.RecipientId == userId || x.SenderId == userId)
                && x.RelationshipId == member.CoupleRelationshipId)
            .OrderByDescending(x => x.Id).Skip((Math.Clamp(page, 1, 1000000) - 1) * PageSize)
            .Take(PageSize + 1).ToListAsync(ct);
    }

    public async Task<string?> SendAsync(int userId, string? content, CancellationToken ct = default) {
        content = content?.Trim();
        if (string.IsNullOrEmpty(content)) return "先写下想对对方说的话吧。";
        if (content.Length > 2000) return "留言最多可以写 2000 字。";
        var member = await MemberAsync(userId, ct);
        if (member is null) return "当前账号没有有效的情侣关系，暂时不能留言。";
        var recipients = await db.Users.Where(x => x.Id != userId && x.IsActive
            && x.CoupleRelationshipId == member.CoupleRelationshipId
            && (x.Role == UserRole.Boy || x.Role == UserRole.Girl))
            .Select(x => x.Id).Take(2).ToListAsync(ct);
        if (recipients.Count != 1) return "暂时无法确认收件的对方，请检查双方账号和情侣关系。";
        db.Set<PrivateMessage>().Add(new PrivateMessage {
            RelationshipId = member.CoupleRelationshipId!.Value,
            SenderId = userId, RecipientId = recipients[0], Content = content
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default) {
        var member = await MemberAsync(userId, ct);
        if (member is null) return false;
        return await db.Set<PrivateMessage>()
            .Where(x => x.Id == id && x.SenderId == userId && x.RelationshipId == member.CoupleRelationshipId)
            .ExecuteDeleteAsync(ct) == 1;
    }
}
