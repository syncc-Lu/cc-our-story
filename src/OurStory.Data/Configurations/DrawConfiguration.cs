using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OurStory.Core.Entities;

namespace OurStory.Data.Configurations;

public sealed class DrawGameConfiguration : IEntityTypeConfiguration<DrawGame> {
    public void Configure(EntityTypeBuilder<DrawGame> builder) {
        builder.ToTable("draw_games");
        builder.HasKey(x => x.RelationshipId);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.StateJson).IsRequired();
        builder.HasOne<CoupleRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.PlayerOneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.PlayerTwoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DrawWordConfiguration : IEntityTypeConfiguration<DrawWord> {
    public void Configure(EntityTypeBuilder<DrawWord> builder) {
        builder.ToTable("draw_words");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Answer).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Aliases).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.RelationshipId, x.SourceKey }).IsUnique();
        builder.HasOne<CoupleRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.Cascade);
    }
}
