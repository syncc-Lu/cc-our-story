namespace OurStory.Core.Entities;

/// <summary>每段情侣关系的一盘在线五子棋，棋谱和版本号持久化。</summary>
public sealed class GomokuGame {
    public int RelationshipId { get; set; }
    public int BlackUserId { get; set; }
    public int WhiteUserId { get; set; }
    public string MovesJson { get; set; } = "[]";
    public int Outcome { get; set; }
    public int Version { get; set; }
}
