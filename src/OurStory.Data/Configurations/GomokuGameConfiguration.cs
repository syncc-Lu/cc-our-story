using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OurStory.Core.Entities;

namespace OurStory.Data.Configurations;

public sealed class GomokuGameConfiguration : IEntityTypeConfiguration<GomokuGame> {
    public void Configure(EntityTypeBuilder<GomokuGame> builder) {
        builder.ToTable("gomoku_games");
        builder.HasKey(x => x.RelationshipId);
        builder.Property(x => x.MovesJson).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne<CoupleRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.BlackUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.WhiteUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
