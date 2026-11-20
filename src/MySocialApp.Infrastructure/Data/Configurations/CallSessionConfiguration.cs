using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class CallSessionConfiguration : IEntityTypeConfiguration<CallSession>
{
    public void Configure(EntityTypeBuilder<CallSession> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(c => c.Conversation)
            .WithMany()
            .HasForeignKey(c => c.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Caller)
            .WithMany()
            .HasForeignKey(c => c.CallerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Callee)
            .WithMany()
            .HasForeignKey(c => c.CalleeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.ConversationId, c.CreatedAt });
        builder.HasIndex(c => new { c.CallerId, c.Status });
        builder.HasIndex(c => new { c.CalleeId, c.Status });
    }
}
