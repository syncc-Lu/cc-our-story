namespace OurStory.Core.Entities;

/// <summary>情侣关系独立维护的画猜词条，内置词删除后保留墓碑，避免重新导入。</summary>
public sealed class DrawWord {
    public int Id { get; set; }
    public int RelationshipId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Aliases { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Difficulty { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool IsDeleted { get; set; }
}
