using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Infrastructure.Data.Configurations;

public class ChatPollVoteConfiguration : IEntityTypeConfiguration<ChatPollVote>
{
    public void Configure(EntityTypeBuilder<ChatPollVote> builder)
    {
        builder.HasKey(v => v.Id);

        builder.HasOne(v => v.Poll)
            .WithMany(p => p.Votes)
            .HasForeignKey(v => v.PollId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(v => v.Option)
            .WithMany(o => o.Votes)
            .HasForeignKey(v => v.OptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.OptionId, v.UserId }).IsUnique();
        builder.HasIndex(v => new { v.PollId, v.UserId });
    }
}
