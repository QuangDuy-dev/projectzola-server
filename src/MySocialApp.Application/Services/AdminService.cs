using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Admin;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Shops;
using MySocialApp.Application.DTOs.Users;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class AdminService : IAdminService
{
    private readonly IApplicationDbContext _context;
    private readonly PasswordHasher<User> _passwordHasher;

    public AdminService(IApplicationDbContext context)
    {
        _context = context;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<PagedResult<UserDto>> GetAllUsersAsync(
        PaginationParams pagination,
        string? q = null,
        string? role = null,
        bool? isActive = null)
    {
        var query = _context.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var lowerQ = q.Trim().ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(lowerQ) ||
                u.Email.ToLower().Contains(lowerQ) ||
                u.DisplayName.ToLower().Contains(lowerQ));
        }

        if (!string.IsNullOrWhiteSpace(role) && !string.Equals(role.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<UserRole>(role.Trim(), true, out var parsedRole))
            {
                query = query.Where(u => u.Role == parsedRole);
            }
            else
            {
                query = query.Where(_ => false);
            }
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        var orderedQuery = query.OrderByDescending(u => u.CreatedAt);
        var totalItems = await orderedQuery.CountAsync();

        var dtos = await orderedQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(user => new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                Bio = user.Bio,
                Role = user.Role.ToString(),
                IsActive = user.IsActive,
                FollowerCount = _context.Follows.Count(f => f.FollowingId == user.Id),
                FollowingCount = _context.Follows.Count(f => f.FollowerId == user.Id),
                PostCount = _context.Posts.Count(p => p.UserId == user.Id),
                CreatedAt = user.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<UserDto>
        {
            Items = dtos,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<UserDto> GetUserByIdAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        return await MapToUserDtoAsync(user);
    }

    public async Task<AdminUserDetailDto> GetUserDetailAsync(Guid id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new KeyNotFoundException("User not found.");

        var postsCount = await _context.Posts.CountAsync(p => p.UserId == id);
        var followersCount = await _context.Follows.CountAsync(f => f.FollowingId == id);
        var followingCount = await _context.Follows.CountAsync(f => f.FollowerId == id);

        var buyerOrdersQuery = _context.Orders.AsNoTracking().Where(o => o.BuyerId == id);
        var buyerOrdersCount = await buyerOrdersQuery.CountAsync();
        var totalSpent = await buyerOrdersQuery
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)(o.TotalPrice + o.ShippingFee)) ?? 0m;

        var recentOrders = await buyerOrdersQuery
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .Select(o => new AdminUserOrderSummaryDto
            {
                Id = o.Id,
                ShopId = o.ShopId,
                ShopName = o.Shop.Name,
                TotalPrice = o.TotalPrice,
                ShippingFee = o.ShippingFee,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt
            })
            .ToListAsync();

        var detail = new AdminUserDetailDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            PostsCount = postsCount,
            FollowersCount = followersCount,
            FollowingCount = followingCount,
            BuyerOrdersCount = buyerOrdersCount,
            TotalSpent = totalSpent,
            RecentOrders = recentOrders
        };

        if (user.Role == UserRole.ShopOwner)
        {
            detail.Shops = await _context.Shops
                .AsNoTracking()
                .Where(s => s.OwnerId == id)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new AdminShopSummaryDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    IsActive = s.IsActive,
                    ProductCount = s.Products.Count,
                    OrderCount = s.Orders.Count,
                    TotalRevenue = s.Orders
                        .Where(o => o.Status == OrderStatus.Delivered)
                        .Sum(o => (decimal?)o.TotalPrice) ?? 0m
                })
                .ToListAsync();
        }
        else if (user.Role == UserRole.Shipper)
        {
            var profile = await _context.ShipperProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(sp => sp.UserId == id);

            var totalDeliveries = await _context.Orders
                .CountAsync(o => o.ShipperId == id && o.Status == OrderStatus.Delivered);

            var activeDeliveriesCount = await _context.Orders
                .CountAsync(o => o.ShipperId == id &&
                    (o.Status == OrderStatus.Shipping || o.Status == OrderStatus.WaitingForPickup));

            detail.ShipperProfile = new AdminShipperProfileDto
            {
                VehicleType = profile?.VehicleType,
                LicensePlate = profile?.VehiclePlate,
                IsAvailable = profile?.IsAvailable ?? false,
                TotalDeliveries = totalDeliveries
            };
            detail.ActiveDeliveriesCount = activeDeliveriesCount;
        }
        else if (user.Role == UserRole.Admin)
        {
            detail.AdminPermissions = new List<string>
            {
                "users:read",
                "users:create",
                "users:update",
                "users:delete",
                "shops:manage",
                "posts:moderate",
                "orders:view",
                "revenue:view"
            };
            detail.AdminPermissionsSummary = "Full system administration access across Users, Shops, Posts, Orders, and Revenue.";
        }

        return detail;
    }

    public async Task<UserDto> CreateUserAsync(AdminCreateUserRequest request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();

        if (await _context.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Username already exists.");

        if (await _context.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("Email already exists.");

        if (!Enum.TryParse<UserRole>(request.Role.Trim(), true, out var role))
            throw new ArgumentException($"Invalid role: {request.Role}");

        var user = new User
        {
            Username = username,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            Bio = request.Bio?.Trim(),
            Role = role,
            IsActive = true
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _context.Users.Add(user);

        if (role == UserRole.ShopOwner && !string.IsNullOrWhiteSpace(request.ShopName))
        {
            var shop = new Shop
            {
                OwnerId = user.Id,
                Name = request.ShopName.Trim(),
                IsActive = true
            };
            _context.Shops.Add(shop);
        }

        if (role == UserRole.Shipper)
        {
            var shipperProfile = new ShipperProfile
            {
                UserId = user.Id,
                VehicleType = request.VehicleType?.Trim(),
                VehiclePlate = request.LicensePlate?.Trim(),
                IsAvailable = true
            };
            _context.ShipperProfiles.Add(shipperProfile);
        }

        await _context.SaveChangesAsync();
        return await MapToUserDtoAsync(user);
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, AdminUpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName.Trim();

        if (request.Email != null)
        {
            var trimmedEmail = request.Email.Trim();
            if (!string.Equals(user.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase) &&
                await _context.Users.AnyAsync(u => u.Email == trimmedEmail && u.Id != id))
            {
                throw new InvalidOperationException("Email already exists.");
            }
            user.Email = trimmedEmail;
        }

        if (request.Bio != null)
            user.Bio = request.Bio.Trim();

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!Enum.TryParse<UserRole>(request.Role.Trim(), true, out var role))
                throw new ArgumentException($"Invalid role: {request.Role}");
            user.Role = role;

            if (role == UserRole.ShopOwner && !await _context.Shops.AnyAsync(s => s.OwnerId == user.Id))
            {
                var defaultShopName = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? $"Shop của {user.Username}"
                    : $"Shop của {user.DisplayName}";

                _context.Shops.Add(new Shop
                {
                    OwnerId = user.Id,
                    Name = defaultShopName,
                    IsActive = true
                });
            }

            if (role == UserRole.Shipper && !await _context.ShipperProfiles.AnyAsync(sp => sp.UserId == user.Id))
            {
                _context.ShipperProfiles.Add(new ShipperProfile
                {
                    UserId = user.Id,
                    IsAvailable = true
                });
            }
        }

        var shouldRevokeSessions = false;

        if (request.IsActive.HasValue)
        {
            if (user.IsActive && !request.IsActive.Value)
                shouldRevokeSessions = true;
            user.IsActive = request.IsActive.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            shouldRevokeSessions = true;
        }

        if (shouldRevokeSessions)
        {
            user.TokenVersion++;
            await RevokeAllUserSessionsAsync(user.Id);
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await MapToUserDtoAsync(user);
    }

    public async Task DeleteUserAsync(Guid id, bool permanent = false, Guid? currentAdminId = null)
    {
        if (currentAdminId.HasValue && currentAdminId.Value == id)
            throw new InvalidOperationException("Cannot delete your own account.");

        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.Role == UserRole.Admin && user.IsActive)
        {
            var otherActiveAdmins = await _context.Users
                .CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != id);
            if (otherActiveAdmins == 0)
                throw new InvalidOperationException("Cannot delete the last active admin.");
        }

        if (!permanent)
        {
            user.IsActive = false;
            user.TokenVersion++;
            user.UpdatedAt = DateTime.UtcNow;
            await RevokeAllUserSessionsAsync(user.Id);
            await _context.SaveChangesAsync();
            return;
        }

        // Permanent deletion: clean up dependent records to satisfy Restrict foreign keys
        var follows = await _context.Follows
            .Where(f => f.FollowerId == id || f.FollowingId == id)
            .ToListAsync();
        _context.Follows.RemoveRange(follows);

        var reactions = await _context.PostReactions
            .Where(r => r.UserId == id)
            .ToListAsync();
        _context.PostReactions.RemoveRange(reactions);

        var userPosts = await _context.Posts
            .IgnoreQueryFilters()
            .Where(p => p.UserId == id)
            .ToListAsync();
        var userPostIds = userPosts.Select(p => p.Id).ToList();

        var commentsToRemove = await _context.Comments
            .IgnoreQueryFilters()
            .Where(c => c.UserId == id || userPostIds.Contains(c.PostId))
            .ToListAsync();
        var commentIdsToRemove = commentsToRemove.Select(c => c.Id).ToList();

        if (commentIdsToRemove.Count > 0)
        {
            var childReplies = await _context.Comments
                .IgnoreQueryFilters()
                .Where(c => c.ParentCommentId.HasValue && commentIdsToRemove.Contains(c.ParentCommentId.Value) && !commentIdsToRemove.Contains(c.Id))
                .ToListAsync();
            foreach (var reply in childReplies)
            {
                reply.ParentCommentId = null;
            }

            foreach (var comment in commentsToRemove)
            {
                comment.ParentCommentId = null;
            }
            _context.Comments.RemoveRange(commentsToRemove);
        }

        _context.Posts.RemoveRange(userPosts);

        var userMessageIds = await _context.Messages
            .Where(m => m.SenderId == id)
            .Select(m => m.Id)
            .ToListAsync();

        if (userMessageIds.Count > 0)
        {
            var membersWithLastRead = await _context.ConversationMembers
                .Where(cm => cm.LastReadMessageId.HasValue && userMessageIds.Contains(cm.LastReadMessageId.Value))
                .ToListAsync();
            foreach (var member in membersWithLastRead)
            {
                member.LastReadMessageId = null;
            }

            var userMessages = await _context.Messages
                .Where(m => m.SenderId == id)
                .ToListAsync();
            _context.Messages.RemoveRange(userMessages);
        }

        var memberships = await _context.ConversationMembers
            .Where(cm => cm.UserId == id)
            .ToListAsync();
        _context.ConversationMembers.RemoveRange(memberships);

        var shipperOrders = await _context.Orders
            .Where(o => o.ShipperId == id)
            .ToListAsync();
        foreach (var order in shipperOrders)
        {
            order.ShipperId = null;
        }

        var buyerOrders = await _context.Orders
            .Where(o => o.BuyerId == id)
            .ToListAsync();
        _context.Orders.RemoveRange(buyerOrders);

        var userShops = await _context.Shops
            .Where(s => s.OwnerId == id)
            .ToListAsync();
        if (userShops.Count > 0)
        {
            var shopIds = userShops.Select(s => s.Id).ToList();
            var shopOrders = await _context.Orders
                .Where(o => shopIds.Contains(o.ShopId))
                .ToListAsync();
            _context.Orders.RemoveRange(shopOrders);

            var shopProducts = await _context.Products
                .Where(p => shopIds.Contains(p.ShopId))
                .ToListAsync();
            if (shopProducts.Count > 0)
            {
                var productIds = shopProducts.Select(p => p.Id).ToList();
                var cartItems = await _context.CartItems
                    .Where(ci => productIds.Contains(ci.ProductId))
                    .ToListAsync();
                _context.CartItems.RemoveRange(cartItems);

                var orderItems = await _context.OrderItems
                    .Where(oi => productIds.Contains(oi.ProductId))
                    .ToListAsync();
                _context.OrderItems.RemoveRange(orderItems);

                _context.Products.RemoveRange(shopProducts);
            }

            _context.Shops.RemoveRange(userShops);
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
    }

    public async Task<UserDto> ToggleUserActiveAsync(Guid id, Guid? currentAdminId = null)
    {
        if (currentAdminId.HasValue && currentAdminId.Value == id)
            throw new InvalidOperationException("Cannot deactivate your own account.");

        var user = await _context.Users.FindAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.IsActive && user.Role == UserRole.Admin)
        {
            var otherActiveAdmins = await _context.Users
                .CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != id);
            if (otherActiveAdmins == 0)
                throw new InvalidOperationException("Cannot deactivate the last active admin.");
        }

        user.IsActive = !user.IsActive;
        if (!user.IsActive)
        {
            user.TokenVersion++;
            await RevokeAllUserSessionsAsync(user.Id);
        }
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await MapToUserDtoAsync(user);
    }

    private async Task RevokeAllUserSessionsAsync(Guid userId)
    {
        var activeSessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var session in activeSessions)
        {
            session.RevokedAt = now;
        }
    }

    public async Task<PagedResult<PostDto>> GetAllPostsAsync(PaginationParams pagination)
    {
        var query = _context.Posts
            .IgnoreQueryFilters()
            .Include(p => p.User)
            .Include(p => p.Media)
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .OrderByDescending(p => p.CreatedAt);

        var totalItems = await query.CountAsync();
        var posts = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var dtos = posts.Select(p => new PostDto
        {
            Id = p.Id,
            UserId = p.UserId,
            Username = p.User.Username,
            DisplayName = p.User.DisplayName,
            UserAvatarUrl = p.User.AvatarUrl,
            Content = p.Content,
            LikeCount = p.Reactions.Count(r => r.ReactionType == ReactionType.Like),
            DislikeCount = p.Reactions.Count(r => r.ReactionType == ReactionType.Dislike),
            CommentCount = p.Comments.Count,
            Media = p.Media.Select(m => new PostMediaDto
            {
                Id = m.Id,
                MediaType = m.MediaType.ToString(),
                StorageUrl = m.StorageUrl,
                ThumbnailUrl = m.ThumbnailUrl
            }).ToList(),
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }).ToList();

        return new PagedResult<PostDto>
        {
            Items = dtos,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task DeletePostAsync(Guid id)
    {
        var post = await _context.Posts.FindAsync(id)
            ?? throw new KeyNotFoundException("Post not found.");
        post.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    public async Task<PagedResult<ShopDto>> GetAllShopsAsync(PaginationParams pagination)
    {
        var query = _context.Shops
            .Include(s => s.Owner)
            .Include(s => s.Products)
            .OrderByDescending(s => s.CreatedAt);

        var totalItems = await query.CountAsync();
        var shops = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var dtos = shops.Select(s => new ShopDto
        {
            Id = s.Id,
            OwnerId = s.OwnerId,
            OwnerDisplayName = s.Owner.DisplayName,
            Name = s.Name,
            Description = s.Description,
            AvatarUrl = s.AvatarUrl,
            IsActive = s.IsActive,
            ProductCount = s.Products.Count,
            CreatedAt = s.CreatedAt
        }).ToList();

        return new PagedResult<ShopDto>
        {
            Items = dtos,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ShopDto> ToggleShopActiveAsync(Guid id)
    {
        var shop = await _context.Shops
            .Include(s => s.Owner)
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new KeyNotFoundException("Shop not found.");

        shop.IsActive = !shop.IsActive;
        shop.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new ShopDto
        {
            Id = shop.Id,
            OwnerId = shop.OwnerId,
            OwnerDisplayName = shop.Owner.DisplayName,
            Name = shop.Name,
            Description = shop.Description,
            AvatarUrl = shop.AvatarUrl,
            IsActive = shop.IsActive,
            ProductCount = shop.Products.Count,
            CreatedAt = shop.CreatedAt
        };
    }

    private async Task<UserDto> MapToUserDtoAsync(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            FollowerCount = await _context.Follows.CountAsync(f => f.FollowingId == user.Id),
            FollowingCount = await _context.Follows.CountAsync(f => f.FollowerId == user.Id),
            PostCount = await _context.Posts.CountAsync(p => p.UserId == user.Id),
            CreatedAt = user.CreatedAt
        };
    }
}
