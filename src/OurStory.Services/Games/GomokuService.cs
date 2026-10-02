using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OurStory.Core;
using OurStory.Core.Entities;
using OurStory.Data;

namespace OurStory.Services.Games;

public sealed record GomokuState(int Version, int MyColor, int[] Moves, int Outcome);
public sealed record GomokuResult(bool Ok, string? Message, GomokuState? Game);

public sealed class GomokuService(OurStoryDbContext db, GomokuUpdates updates) {
    private async Task<(int RelationshipId, int PartnerId)?> PairAsync(int userId, CancellationToken ct) {
        var member = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive
            && (x.Role == UserRole.Boy || x.Role == UserRole.Girl)
            && x.CoupleRelationshipId != null && x.CoupleRelationship!.IsActive, ct);
        if (member is null) return null;
        var partners = await db.Users.AsNoTracking().Where(x => x.Id != userId && x.IsActive
            && (x.Role == UserRole.Boy || x.Role == UserRole.Girl)
            && x.CoupleRelationshipId == member.CoupleRelationshipId).Select(x => x.Id).Take(2).ToListAsync(ct);
        return partners.Count == 1 ? (member.CoupleRelationshipId!.Value, partners[0]) : null;
    }

    private static GomokuState State(GomokuGame game, int userId) => new(game.Version,
        game.BlackUserId == userId ? 1 : 2, JsonSerializer.Deserialize<int[]>(game.MovesJson)!, game.Outcome);

    private static bool ParticipantsMatch(GomokuGame game, int userId, int partnerId) =>
        (game.BlackUserId == userId && game.WhiteUserId == partnerId)
        || (game.WhiteUserId == userId && game.BlackUserId == partnerId);

    public async Task<GomokuResult> GetAsync(int userId, CancellationToken ct = default) {
        var pair = await PairAsync(userId, ct);
        if (pair is null) return new(false, "在线对战需要有效的情侣双方账号。", null);
        var game = await db.Set<GomokuGame>().AsNoTracking().SingleOrDefaultAsync(x => x.RelationshipId == pair.Value.RelationshipId, ct);
        if (game is not null && !ParticipantsMatch(game, userId, pair.Value.PartnerId))
            return new(false, "对局参与者与当前情侣关系不一致。", null);
        return new(true, null, game is null ? null : State(game, userId));
    }

    public async Task<GomokuResult> ActAsync(int userId, string action, int version, int row = -1, int col = -1, CancellationToken ct = default) {
        var pair = await PairAsync(userId, ct);
        if (pair is null) return new(false, "在线对战需要有效的情侣双方账号。", null);
        var game = await db.Set<GomokuGame>().AsNoTracking().SingleOrDefaultAsync(x => x.RelationshipId == pair.Value.RelationshipId, ct);
        if (game is not null && !ParticipantsMatch(game, userId, pair.Value.PartnerId))
            return new(false, "对局参与者与当前情侣关系不一致。", null);
        if (version != (game?.Version ?? 0)) return new(false, "棋局已更新，请按最新棋盘继续。", game is null ? null : State(game, userId));
        var moves = game is null ? [] : JsonSerializer.Deserialize<List<int>>(game.MovesJson)!;
        var outcome = game?.Outcome ?? 0;
        var black = game?.BlackUserId ?? userId;
        var white = game?.WhiteUserId ?? pair.Value.PartnerId;
        if (action == "start") {
            if (game is not null && outcome == 0) return new(false, "当前对局尚未结束，请先完成或认输。", State(game, userId));
            moves.Clear();
            outcome = 0;
            // 再来一局时交换先手。
            black = game?.WhiteUserId ?? userId;
            white = game?.BlackUserId ?? pair.Value.PartnerId;
        } else {
            if (game is null || outcome != 0) return new(false, "请先开始新的一局。", game is null ? null : State(game, userId));
            var color = black == userId ? 1 : 2;
            if (action == "move") {
                if (moves.Count % 2 + 1 != color) return new(false, "还没轮到你，请等对方落子。", State(game, userId));
                if (row < 0 || row >= GomokuRules.Size || col < 0 || col >= GomokuRules.Size)
                    return new(false, "请选择棋盘内的交叉点。", State(game, userId));
                var point = row * GomokuRules.Size + col;
                if (moves.Contains(point)) return new(false, "这里已经有棋子了。", State(game, userId));
                moves.Add(point);
                outcome = GomokuRules.Outcome(moves);
            } else if (action == "resign") {
                outcome = 3 - color;
            } else return new(false, "不支持这个操作。", State(game, userId));
        }
        var next = new GomokuGame { RelationshipId = pair.Value.RelationshipId, BlackUserId = black,
            WhiteUserId = white, MovesJson = JsonSerializer.Serialize(moves), Outcome = outcome, Version = version + 1 };
        if (game is null) {
            db.Set<GomokuGame>().Add(next);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) {
                db.Entry(next).State = EntityState.Detached;
                // 同时开局时只有一方能插入，另一方重新读取已创建的棋局。
                if (await db.Set<GomokuGame>().AnyAsync(x => x.RelationshipId == next.RelationshipId, ct))
                    return new(false, "对方已创建对局，请刷新棋盘。", null);
                throw;
            }
        } else {
            var updated = await db.Set<GomokuGame>().Where(x => x.RelationshipId == game.RelationshipId && x.Version == version)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.MovesJson, next.MovesJson)
                    .SetProperty(x => x.Outcome, next.Outcome).SetProperty(x => x.Version, next.Version)
                    .SetProperty(x => x.BlackUserId, next.BlackUserId).SetProperty(x => x.WhiteUserId, next.WhiteUserId), ct);
            if (updated == 0) return new(false, "棋局已更新，请按最新棋盘继续。", null);
        }
        updates.Publish();
        return new(true, null, State(next, userId));
    }
}
