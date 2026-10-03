using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OurStory.Core.Entities;
using OurStory.Data;

namespace OurStory.Services.Games.Draw;

public sealed class DrawGameService(OurStoryDbContext db, DrawPairAccess access, DrawWordService words,
    DrawCoordinator coordinator, TimeProvider time) {
    public const int Rounds = 6;
    public const int RoundSeconds = 90;
    public static readonly string[] Colors = ["#30303b", "#df728d", "#e85a50", "#e6a23c", "#55a878", "#4a8ecc", "#916bbf", "#ffffff"];
    private static readonly JsonSerializerOptions Json = new();
    private static DrawSession Read(DrawGame game) => JsonSerializer.Deserialize<DrawSession>(game.StateJson, Json)!;
    private static bool Participants(DrawGame game, int userId, int partnerId) =>
        (game.PlayerOneId == userId && game.PlayerTwoId == partnerId) || (game.PlayerTwoId == userId && game.PlayerOneId == partnerId);
    private static DrawResponse Denied() => new(false, "画猜对局仅限当前有效的情侣双方。", null, true);

    private DrawView Project(DrawGame game, DrawSession session, int userId, string epoch, int since) {
        var now = time.GetUtcNow();
        var drawer = session.DrawerId == userId;
        var revealed = session.Stage is "reveal" or "finished";
        var hint = session.Stage == "drawing" && session.EndsAt <= now.AddSeconds(45);
        var start = epoch == session.CanvasEpoch && since >= 0 && since <= session.Strokes.Count ? since : 0;
        return new(game.Version, session.RoundId, session.Round, session.Stage, drawer, now, session.EndsAt,
            drawer || revealed ? session.Answer?.Answer : null,
            hint ? session.Answer?.Category : null, hint ? session.Answer?.Answer.EnumerateRunes().Count() : null,
            drawer && session.Stage == "choosing" ? session.Candidates.Select((x, i) => new DrawChoice(i, x.Answer, x.Category, x.Difficulty)).ToArray() : [],
            session.Results.Count(x => x.Correct), session.Results.ToArray(), session.Guesses.ToArray(),
            session.CanvasEpoch, start, session.Strokes.Count, session.Strokes.Skip(start).ToArray());
    }

    private async Task<bool> SaveAsync(DrawGame game, DrawSession session, bool isNew, CancellationToken ct) {
        game.StateJson = JsonSerializer.Serialize(session, Json);
        var oldVersion = game.Version;
        game.Version++;
        if (isNew) {
            db.Add(game);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) {
                db.Entry(game).State = EntityState.Detached;
                if (await db.Set<DrawGame>().AnyAsync(x => x.RelationshipId == game.RelationshipId, ct)) return false;
                throw;
            }
            db.Entry(game).State = EntityState.Detached;
        } else {
            var changed = await db.Set<DrawGame>().Where(x => x.RelationshipId == game.RelationshipId && x.Version == oldVersion)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.StateJson, game.StateJson).SetProperty(x => x.Version, game.Version)
                    .SetProperty(x => x.PlayerOneId, game.PlayerOneId).SetProperty(x => x.PlayerTwoId, game.PlayerTwoId), ct);
            if (changed == 0) return false;
        }
        coordinator.Publish();
        return true;
    }

    private static void Finish(DrawSession session, bool correct) {
        session.Stage = "reveal";
        session.Results.Add(new(session.Round, session.Answer!.Answer, correct));
    }

    public async Task<DrawResponse> GetAsync(int userId, string epoch = "", int since = 0, CancellationToken ct = default) {
        await coordinator.Gate.WaitAsync(ct);
        try {
            var pair = await access.GetAsync(userId, ct);
            if (pair is null) return Denied();
            var game = await db.Set<DrawGame>().AsNoTracking().SingleOrDefaultAsync(x => x.RelationshipId == pair.RelationshipId, ct);
            if (game is null) return new(true, null, null);
            if (!Participants(game, userId, pair.PartnerId)) return Denied();
            var session = Read(game);
            if (session.Stage == "drawing" && time.GetUtcNow() >= session.EndsAt) {
                Finish(session, false);
                if (!await SaveAsync(game, session, false, ct)) return new(false, "对局已更新，请重试。", null);
            }
            return new(true, null, Project(game, session, userId, epoch, since));
        } finally { coordinator.Gate.Release(); }
    }

    private async Task<List<DrawWordSnapshot>> CandidatesAsync(int relationship, IReadOnlyCollection<string> used, CancellationToken ct) {
        var pool = (await words.PoolAsync(relationship, ct)).Where(x => x.Enabled)
            .Where(x => !used.Contains(DrawText.Normalize(x.Answer)) && !DrawText.Aliases(x.Aliases).Any(a => used.Contains(DrawText.Normalize(a))))
            .DistinctBy(x => DrawText.Normalize(x.Answer)).OrderBy(_ => Guid.NewGuid()).ToList();
        // 有专属词时优先提供一个，其余候选由完整可用词库补足。
        var personal = pool.FirstOrDefault(x => x.SourceKey.StartsWith("custom:", StringComparison.Ordinal));
        if (personal is not null) { pool.Remove(personal); pool.Insert(0, personal); }
        return pool.Select(x => new DrawWordSnapshot(x.Answer, DrawText.Aliases(x.Aliases), x.Category, x.Difficulty)).ToList();
    }

    public async Task<DrawResponse> ActAsync(int userId, DrawCommand command, CancellationToken ct = default) {
        if (!Guid.TryParse(command.RequestId, out _)) return new(false, "操作编号无效，请刷新页面。", null);
        await coordinator.Gate.WaitAsync(ct);
        try {
            var pair = await access.GetAsync(userId, ct);
            if (pair is null) return Denied();
            var game = await db.Set<DrawGame>().AsNoTracking().SingleOrDefaultAsync(x => x.RelationshipId == pair.RelationshipId, ct);
            if (game is not null && !Participants(game, userId, pair.PartnerId)) return Denied();
            var session = game is null ? null : Read(game);
            DrawResponse Failure(string message) => new(false, message, game is null || session is null ? null : Project(game, session, userId, command.CanvasEpoch, command.CanvasSince));
            if (session is not null && session.Commands.Contains(command.RequestId))
                return new(true, null, Project(game!, session, userId, command.CanvasEpoch, command.CanvasSince));
            if (session is not null && session.Stage == "drawing" && time.GetUtcNow() >= session.EndsAt) {
                Finish(session, false);
                if (!await SaveAsync(game!, session, false, ct)) return new(false, "对局已更新，请重试。", null);
            }
            var isNew = game is null;
            if (command.Action == "start") {
                if (session is not null && (session.Stage != "finished" || command.RoundId != session.RoundId)) return Failure("当前对局还未结束，或页面已过期。");
                var candidates = await CandidatesAsync(pair.RelationshipId, [], ct);
                if (candidates.Count < 8) return Failure("至少需要 8 个启用的不同词条，请先到后台补充或启用词库。");
                var first = game?.PlayerTwoId ?? userId;
                game = new DrawGame { RelationshipId = pair.RelationshipId, PlayerOneId = first,
                    PlayerTwoId = first == userId ? pair.PartnerId : userId, Version = game?.Version ?? 0 };
                session = new DrawSession { DrawerId = first, Candidates = candidates.Take(3).ToList() };
            } else {
                if (game is null || session is null) return Failure("请先开始一局。");
                if (command.RoundId != session.RoundId) return Failure("已经换到新一轮，请按当前画面继续。");
                if (session.Stage == "finished") return Failure("本局已结束，请重新开局。");
                switch (command.Action) {
                    case "choose":
                        if (session.DrawerId != userId || session.Stage != "choosing") return Failure("只有本轮画画的人可以选词。");
                        if (command.Choice < 0 || command.Choice >= session.Candidates.Count) return Failure("请选择提供的候选词。");
                        session.Answer = session.Candidates[command.Choice];
                        session.Used.Add(DrawText.Normalize(session.Answer.Answer));
                        session.Used.AddRange(session.Answer.Aliases.Select(DrawText.Normalize));
                        session.Candidates.Clear(); session.Stage = "drawing";
                        session.EndsAt = time.GetUtcNow().AddSeconds(RoundSeconds);
                        break;
                    case "ink":
                        if (session.DrawerId != userId || session.Stage != "drawing") return Failure("当前不能画画。");
                        if (command.CanvasEpoch != session.CanvasEpoch) return Failure("画布已更新，请在当前画布上继续。");
                        var stroke = command.Stroke;
                        if (stroke is null || stroke.Id != command.RequestId || !Colors.Contains(stroke.Color) || stroke.Width is < 1 or > 30
                            || (stroke.GestureId is not null && !Guid.TryParse(stroke.GestureId, out _))
                            || stroke.Points is null || stroke.Points.Length is < 1 or > 48
                            || stroke.Points.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is < 0 or > 1000 || p.Y is < 0 or > 700))
                            return Failure("笔画数据无效。");
                        if (session.Strokes.Count >= 1200) return Failure("这一轮笔画较多，请清空画布后继续。");
                        session.Strokes.Add(stroke);
                        break;
                    case "undo":
                        if (session.DrawerId != userId || session.Stage != "drawing") return Failure("只有本轮画画的人可以撤回。");
                        if (command.CanvasEpoch != session.CanvasEpoch) return Failure("画布已更新，请重试。");
                        if (session.Strokes.Count == 0) return Failure("还没有可以撤回的笔画。");
                        // 同一次落笔的实时分段一起移除；旧客户端的笔画按单段兼容。
                        var last = session.Strokes[^1];
                        var first = session.Strokes.Count - 1;
                        if (last.GestureId is not null)
                            while (first > 0 && session.Strokes[first - 1].GestureId == last.GestureId) first--;
                        session.Strokes.RemoveRange(first, session.Strokes.Count - first);
                        session.CanvasEpoch = Guid.NewGuid().ToString("N");
                        break;
                    case "clear":
                        if (session.DrawerId != userId || session.Stage != "drawing") return Failure("只有本轮画画的人可以清空画布。");
                        session.Strokes.Clear(); session.CanvasEpoch = Guid.NewGuid().ToString("N");
                        break;
                    case "guess":
                        if (session.DrawerId == userId || session.Stage != "drawing") return Failure("现在不是你的猜词时间。");
                        var guess = (command.Text ?? string.Empty).Trim();
                        if (guess.Length is < 1 or > 48 || guess.EnumerateRunes().Count() > 24) return Failure("请填写 1 到 24 个中文字符的答案。");
                        if (session.Guesses.Count >= 100) return Failure("本轮已提交 100 次猜测，请等待下一轮。");
                        var normalized = DrawText.Normalize(guess);
                        var correct = normalized == DrawText.Normalize(session.Answer!.Answer)
                            || session.Answer.Aliases.Any(x => DrawText.Normalize(x) == normalized);
                        session.Guesses.Add(new(guess, correct));
                        if (correct) Finish(session, true);
                        break;
                    case "next":
                        if (session.Stage != "reveal") return Failure("请等本轮结束后再继续。");
                        if (session.Round == Rounds) session.Stage = "finished";
                        else {
                            var candidates = await CandidatesAsync(pair.RelationshipId, session.Used, ct);
                            if (candidates.Count < 3) return Failure("可用新词不足，请到后台补充词库后继续。");
                            session.Round++; session.RoundId = Guid.NewGuid().ToString("N");
                            session.DrawerId = session.DrawerId == game.PlayerOneId ? game.PlayerTwoId : game.PlayerOneId;
                            session.Stage = "choosing"; session.Answer = null; session.EndsAt = null;
                            session.Candidates = candidates.Take(3).ToList(); session.Guesses.Clear(); session.Strokes.Clear();
                            session.CanvasEpoch = Guid.NewGuid().ToString("N");
                        }
                        break;
                    case "end":
                        if (session.Stage == "drawing") Finish(session, false);
                        session.Stage = "finished";
                        break;
                    default: return Failure("不支持这个操作。");
                }
            }
            session!.Commands.Add(command.RequestId);
            if (session.Commands.Count > 256) session.Commands.RemoveRange(0, session.Commands.Count - 256);
            if (!await SaveAsync(game!, session, isNew, ct)) return new(false, "对局刚刚发生变化，请重试。", null);
            return new(true, null, Project(game!, session, userId, command.CanvasEpoch, command.CanvasSince));
        } finally { coordinator.Gate.Release(); }
    }
}
