using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OurStory.Core.Entities;
using OurStory.Data;

namespace OurStory.Services.Games.Draw;

public sealed record BuiltInDrawWord(string Key, string Answer, string[] Aliases, string Category, int Difficulty);
public sealed record DrawWordInput(string Answer, string Aliases, string Category, int Difficulty, bool Enabled);
public sealed record DrawWordPage(IReadOnlyList<DrawWord> Items, int Total, int Page);

public sealed class DrawWordService(OurStoryDbContext db, DrawPairAccess access, DrawCoordinator coordinator) {
    public static IReadOnlyList<BuiltInDrawWord> BuiltIns { get; } = LoadBuiltIns();
    private static BuiltInDrawWord[] LoadBuiltIns() {
        using var stream = typeof(DrawWordService).Assembly.GetManifestResourceStream("OurStory.DrawWords.json")!;
        return JsonSerializer.Deserialize<BuiltInDrawWord[]>(stream)!;
    }

    // 调用方持有 coordinator.Gate，且已取得有效的情侣关系。
    internal async Task<List<DrawWord>> PoolAsync(int relationshipId, CancellationToken ct) {
        var existing = await db.Set<DrawWord>().AsNoTracking().Where(x => x.RelationshipId == relationshipId)
            .Select(x => x.SourceKey).ToListAsync(ct);
        var keys = existing.ToHashSet(StringComparer.Ordinal);
        var missing = BuiltIns.Where(x => !keys.Contains(x.Key)).Select(x => new DrawWord {
            RelationshipId = relationshipId, SourceKey = x.Key, Answer = x.Answer,
            Aliases = string.Join('\n', x.Aliases), Category = x.Category, Difficulty = x.Difficulty
        }).ToArray();
        if (missing.Length > 0) {
            db.AddRange(missing);
            await db.SaveChangesAsync(ct);
            foreach (var item in missing) db.Entry(item).State = EntityState.Detached;
        }
        return await db.Set<DrawWord>().AsNoTracking().Where(x => x.RelationshipId == relationshipId && !x.IsDeleted).ToListAsync(ct);
    }

    public async Task<DrawWordPage?> ListAsync(int userId, string? search, int page, CancellationToken ct = default) {
        await coordinator.Gate.WaitAsync(ct);
        try {
            var pair = await access.GetAsync(userId, ct);
            if (pair is null) return null;
            var words = await PoolAsync(pair.RelationshipId, ct);
            var query = words.Where(x => string.IsNullOrWhiteSpace(search) || x.Answer.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Category.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Aliases.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SourceKey.StartsWith("builtin:", StringComparison.Ordinal)).ThenBy(x => x.Id).ToArray();
            page = Math.Clamp(page, 1, Math.Max(1, (query.Length + 19) / 20));
            return new(query.Skip((page - 1) * 20).Take(20).ToArray(), query.Length, page);
        } finally { coordinator.Gate.Release(); }
    }

    public async Task<DrawWord?> GetAsync(int userId, int id, CancellationToken ct = default) {
        var pair = await access.GetAsync(userId, ct);
        return pair is null ? null : await db.Set<DrawWord>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.RelationshipId == pair.RelationshipId && !x.IsDeleted, ct);
    }

    public async Task<string?> SaveAsync(int userId, int id, DrawWordInput input, CancellationToken ct = default) {
        var answer = input.Answer.Trim();
        var aliases = DrawText.Aliases(input.Aliases);
        if (answer.Length is < 1 or > 48 || answer.EnumerateRunes().Count() > 24) return "答案请填写 1 到 24 个字。";
        if (input.Category.Trim().Length is < 1 or > 20) return "分类请填写 1 到 20 个字。";
        if (input.Difficulty is < 1 or > 3) return "请选择有效的难度。";
        if (input.Aliases.Length > 500 || aliases.Length > 10 || aliases.Any(x => x.EnumerateRunes().Count() > 24)) return "别名最多 10 个，每个最多 24 个中文字符，总长度不超过 500 字。";
        await coordinator.Gate.WaitAsync(ct);
        try {
            var pair = await access.GetAsync(userId, ct);
            if (pair is null) return "当前账号没有有效的情侣关系。";
            var pool = await PoolAsync(pair.RelationshipId, ct);
            if (pool.Any(x => x.Id != id && DrawText.Normalize(x.Answer) == DrawText.Normalize(answer))) return "这个答案已经在词库中，请编辑已有词条。";
            DrawWord? word;
            if (id == 0) {
                word = new DrawWord { RelationshipId = pair.RelationshipId, SourceKey = "custom:" + Guid.NewGuid().ToString("N") };
                db.Add(word);
            } else {
                word = await db.Set<DrawWord>().SingleOrDefaultAsync(x => x.Id == id && x.RelationshipId == pair.RelationshipId && !x.IsDeleted, ct);
                if (word is null) return "词条不存在或无权编辑。";
            }
            word.Answer = answer; word.Aliases = string.Join('\n', aliases); word.Category = input.Category.Trim();
            word.Difficulty = input.Difficulty; word.Enabled = input.Enabled;
            await db.SaveChangesAsync(ct);
            db.Entry(word).State = EntityState.Detached;
            return null;
        } finally { coordinator.Gate.Release(); }
    }

    public async Task<bool> RemoveAsync(int userId, int id, bool delete, CancellationToken ct = default) {
        await coordinator.Gate.WaitAsync(ct);
        try {
            var pair = await access.GetAsync(userId, ct);
            if (pair is null) return false;
            var word = await db.Set<DrawWord>().SingleOrDefaultAsync(x => x.Id == id && x.RelationshipId == pair.RelationshipId && !x.IsDeleted, ct);
            if (word is null) return false;
            if (delete) word.IsDeleted = true;
            else word.Enabled = !word.Enabled;
            await db.SaveChangesAsync(ct);
            db.Entry(word).State = EntityState.Detached;
            return true;
        } finally { coordinator.Gate.Release(); }
    }
}
