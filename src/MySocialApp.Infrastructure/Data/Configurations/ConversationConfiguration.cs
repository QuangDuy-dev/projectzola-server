using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ConversationType.Direct)
            .ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.AvatarUrl).HasMaxLength(500);
        builder.Property(c => c.InviteCode).HasMaxLength(64);
        builder.Property(c => c.IsInviteLinkEnabled).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(c => c.RequireJoinApproval).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(c => c.OnlyAdminsCanSend).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(c => c.IsDisbanded).HasDefaultValue(false).ValueGeneratedNever();

        builder.HasOne(c => c.CreatedBy)
            .WithMany()
            .HasForeignKey(c => c.CreatedById)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(c => c.InviteCode)
            .IsUnique()
            .HasFilter("[InviteCode] IS NOT NULL");

        builder.HasIndex(c => c.Type);
    }
}
