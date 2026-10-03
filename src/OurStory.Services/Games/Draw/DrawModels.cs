using System.Text;

namespace OurStory.Services.Games.Draw;

public sealed record DrawWordSnapshot(string Answer, string[] Aliases, string Category, int Difficulty);
public sealed record DrawPoint(double X, double Y);
public sealed record DrawStroke(string Id, string Color, int Width, bool Eraser, DrawPoint[] Points, string? GestureId = null);
public sealed record DrawGuess(string Text, bool Correct);
public sealed record DrawRoundResult(int Round, string Answer, bool Correct);

/// <summary>只在服务器保存，禁止直接作为接口响应。</summary>
public sealed class DrawSession {
    public string RoundId { get; set; } = Guid.NewGuid().ToString("N");
    public int Round { get; set; } = 1;
    public int DrawerId { get; set; }
    public string Stage { get; set; } = "choosing";
    public DateTimeOffset? EndsAt { get; set; }
    public List<DrawWordSnapshot> Candidates { get; set; } = [];
    public DrawWordSnapshot? Answer { get; set; }
    public List<string> Used { get; set; } = [];
    public string CanvasEpoch { get; set; } = Guid.NewGuid().ToString("N");
    public List<DrawStroke> Strokes { get; set; } = [];
    public List<DrawGuess> Guesses { get; set; } = [];
    public List<DrawRoundResult> Results { get; set; } = [];
    public List<string> Commands { get; set; } = [];
}

public sealed class DrawCommand {
    public string Action { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string RoundId { get; set; } = string.Empty;
    public int Choice { get; set; } = -1;
    public string Text { get; set; } = string.Empty;
    public DrawStroke? Stroke { get; set; }
    public string CanvasEpoch { get; set; } = string.Empty;
    public int CanvasSince { get; set; }
}

// 投影中不含别名、已用词或原始 session；候选词、答案和提示按角色与阶段分别提供。
public sealed record DrawChoice(int Index, string Answer, string Category, int Difficulty);
public sealed record DrawView(int Version, string RoundId, int Round, string Stage, bool IsDrawer,
    DateTimeOffset ServerNow, DateTimeOffset? EndsAt, string? Answer, string? HintCategory, int? HintLength,
    DrawChoice[] Choices, int Score, DrawRoundResult[] Results, DrawGuess[] Guesses,
    string CanvasEpoch, int StrokeBase, int StrokeCount, DrawStroke[] Strokes);
public sealed record DrawResponse(bool Ok, string? Message, DrawView? Game, bool Forbidden = false);

public static class DrawText {
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    public static string[] Aliases(string value) => value.Split(['\n', '\r', ',', '，', '、'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
