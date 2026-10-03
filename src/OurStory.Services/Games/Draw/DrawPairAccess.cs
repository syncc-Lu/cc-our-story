using Microsoft.EntityFrameworkCore;
using OurStory.Core;
using OurStory.Data;

namespace OurStory.Services.Games.Draw;

public sealed record DrawPair(int RelationshipId, int PartnerId);
public sealed class DrawPairAccess(OurStoryDbContext db) {
    public async Task<DrawPair?> GetAsync(int userId, CancellationToken ct = default) {
        var member = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive
            && (x.Role == UserRole.Boy || x.Role == UserRole.Girl)
            && x.CoupleRelationshipId != null && x.CoupleRelationship!.IsActive, ct);
        if (member is null) return null;
        var partners = await db.Users.AsNoTracking().Where(x => x.Id != userId && x.IsActive
            && (x.Role == UserRole.Boy || x.Role == UserRole.Girl)
            && x.CoupleRelationshipId == member.CoupleRelationshipId).Select(x => x.Id).Take(2).ToListAsync(ct);
        return partners.Count == 1 ? new(member.CoupleRelationshipId!.Value, partners[0]) : null;
    }
}
