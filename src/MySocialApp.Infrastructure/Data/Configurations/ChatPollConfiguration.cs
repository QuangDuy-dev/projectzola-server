using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class ChatPollConfiguration : IEntityTypeConfiguration<ChatPoll>
{
    public void Configure(EntityTypeBuilder<ChatPoll> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Question).HasMaxLength(300).IsRequired();
        builder.Property(p => p.AllowMultipleChoices).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(p => p.IsAnonymous).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(p => p.IsClosed).HasDefaultValue(false).ValueGeneratedNever();

        builder.HasOne(p => p.Conversation)
            .WithMany(c => c.Polls)
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.CreatedBy)
            .WithMany()
            .HasForeignKey(p => p.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.ConversationId);
    }
}
