namespace OurStory.Services.Games;

/// <summary>15 路自由五子棋：黑先，无禁手，连续五子或以上获胜。</summary>
public static class GomokuRules {
    public const int Size = 15;
    public static int Outcome(IReadOnlyList<int> moves) {
        var board = new int[Size * Size];
        for (var i = 0; i < moves.Count; i++) {
            var point = moves[i];
            if (point < 0 || point >= board.Length || board[point] != 0)
                throw new ArgumentException("棋谱包含无效落点。", nameof(moves));
            board[point] = i % 2 + 1;
        }
        if (moves.Count == 0) return 0;
        var last = moves[^1];
        var color = board[last];
        foreach (var (dr, dc) in new[] { (0, 1), (1, 0), (1, 1), (1, -1) }) {
            var count = 1;
            foreach (var sign in new[] { -1, 1 }) {
                var row = last / Size + dr * sign;
                var col = last % Size + dc * sign;
                while (row >= 0 && row < Size && col >= 0 && col < Size && board[row * Size + col] == color) {
                    count++;
                    row += dr * sign;
                    col += dc * sign;
                }
            }
            if (count >= 5) return color;
        }
        return moves.Count == Size * Size ? 3 : 0;
    }
}
