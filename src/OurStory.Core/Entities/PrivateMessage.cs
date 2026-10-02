namespace OurStory.Core.Entities;

/// <summary>情侣双方可见的私密留言，保留发送人与接收人以界定参与者。</summary>
public sealed class PrivateMessage {
    public int Id { get; set; }
    public int RelationshipId { get; set; }
    public int SenderId { get; set; }
    public int RecipientId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
