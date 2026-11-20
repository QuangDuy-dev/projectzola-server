using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Content).HasMaxLength(5000).IsRequired();
        builder.Property(m => m.ClientMessageId).HasMaxLength(64);
        builder.Property(m => m.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(MessageType.Text)
            .ValueGeneratedNever();
        builder.Property(m => m.MentionedUserIdsJson).HasMaxLength(4000);
        builder.Property(m => m.MentionAll).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(m => m.IsPinned).HasDefaultValue(false).ValueGeneratedNever();

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Sender)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.ReplyToMessage)
            .WithMany()
            .HasForeignKey(m => m.ReplyToMessageId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(m => m.PinnedBy)
            .WithMany()
            .HasForeignKey(m => m.PinnedById)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(m => m.Poll)
            .WithMany()
            .HasForeignKey(m => m.PollId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(m => m.ConversationId);
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => new { m.ConversationId, m.IsPinned });
        builder.HasIndex(m => m.PollId);
        builder.HasIndex(m => new { m.SenderId, m.ClientMessageId })
            .IsUnique()
            .HasFilter("[ClientMessageId] IS NOT NULL");
    }
}
