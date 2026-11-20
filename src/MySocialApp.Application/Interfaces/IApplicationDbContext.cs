using Microsoft.EntityFrameworkCore;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Follow> Follows { get; }
    DbSet<Post> Posts { get; }
    DbSet<PostMedia> PostMedia { get; }
    DbSet<PostReaction> PostReactions { get; }
    DbSet<Comment> Comments { get; }
    DbSet<Shop> Shops { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<Category> Categories { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<ShipperProfile> ShipperProfiles { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationMember> ConversationMembers { get; }
    DbSet<Message> Messages { get; }
    DbSet<MessageAttachment> MessageAttachments { get; }
    DbSet<MessageReaction> MessageReactions { get; }
    DbSet<GroupJoinRequest> GroupJoinRequests { get; }
    DbSet<ChatPoll> ChatPolls { get; }
    DbSet<ChatPollOption> ChatPollOptions { get; }
    DbSet<ChatPollVote> ChatPollVotes { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<EmailOtp> EmailOtps { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<CallSession> CallSessions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    int SaveChanges();
}
