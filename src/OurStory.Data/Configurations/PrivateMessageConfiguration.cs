using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OurStory.Core.Entities;

namespace OurStory.Data.Configurations;

public sealed class PrivateMessageConfiguration : IEntityTypeConfiguration<PrivateMessage> {
    public void Configure(EntityTypeBuilder<PrivateMessage> builder) {
        builder.ToTable("private_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.RecipientId, x.RelationshipId, x.Id });
        builder.HasOne<CoupleRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
    }
}
