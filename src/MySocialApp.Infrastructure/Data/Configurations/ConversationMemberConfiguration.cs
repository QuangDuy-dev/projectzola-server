using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class ConversationMemberConfiguration : IEntityTypeConfiguration<ConversationMember>
{
    public void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        builder.HasKey(cm => new { cm.ConversationId, cm.UserId });

        builder.Property(cm => cm.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ConversationMemberRole.Member)
            .ValueGeneratedNever();

        builder.Property(cm => cm.Nickname).HasMaxLength(100);

        builder.HasOne(cm => cm.Conversation)
            .WithMany(c => c.Members)
            .HasForeignKey(cm => cm.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cm => cm.User)
            .WithMany(u => u.ConversationMemberships)
            .HasForeignKey(cm => cm.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cm => cm.LastReadMessage)
            .WithMany()
            .HasForeignKey(cm => cm.LastReadMessageId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
