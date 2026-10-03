namespace OurStory.Core.Entities;

/// <summary>一段情侣关系当前的画猜对局，含服务端私有答案和持久化笔画。</summary>
public sealed class DrawGame {
    public int RelationshipId { get; set; }
    public int PlayerOneId { get; set; }
    public int PlayerTwoId { get; set; }
    public string StateJson { get; set; } = "{}";
    public int Version { get; set; }
}
