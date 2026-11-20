using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class ChatPollOptionConfiguration : IEntityTypeConfiguration<ChatPollOption>
{
    public void Configure(EntityTypeBuilder<ChatPollOption> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Text).HasMaxLength(200).IsRequired();

        builder.HasOne(o => o.Poll)
            .WithMany(p => p.Options)
            .HasForeignKey(o => o.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.PollId, o.SortOrder });
    }
}
