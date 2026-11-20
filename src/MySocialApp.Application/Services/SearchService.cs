using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Search;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class SearchService : ISearchService
{
    private readonly IApplicationDbContext _context;

    public SearchService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ProductSearchDto>> SearchProductsAsync(ProductSearchParams searchParams)
    {
        if (searchParams.Page < 1)
            searchParams.Page = 1;

        if (searchParams.PageSize < 1)
            searchParams.PageSize = 20;
        else if (searchParams.PageSize > 50)
            searchParams.PageSize = 50;

        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.Shop.IsActive);

        if (!string.IsNullOrWhiteSpace(searchParams.Q))
        {
            var trimmedQuery = searchParams.Q.Trim();
            if (trimmedQuery.Length > 100)
                throw new ArgumentException("Search query cannot exceed 100 characters.", nameof(searchParams.Q));

            var lowerQuery = trimmedQuery.ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(lowerQuery) || (p.Description != null && p.Description.ToLower().Contains(lowerQuery)));
        }

        if (searchParams.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == searchParams.CategoryId.Value);
        }

        if (searchParams.ShopId.HasValue)
        {
            query = query.Where(p => p.ShopId == searchParams.ShopId.Value);
        }

        if (searchParams.MinPrice.HasValue)
        {
            query = query.Where(p => p.Price >= searchParams.MinPrice.Value);
        }

        if (searchParams.MaxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= searchParams.MaxPrice.Value);
        }

        query = (searchParams.Sort?.ToLowerInvariant()) switch
        {
            "price_asc" => query.OrderBy(p => p.Price).ThenByDescending(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.Price).ThenByDescending(p => p.Id),
            "name" or "name_asc" => query.OrderBy(p => p.Name).ThenByDescending(p => p.Id),
            "name_desc" => query.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((searchParams.Page - 1) * searchParams.PageSize)
            .Take(searchParams.PageSize)
            .Select(p => new ProductSearchDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                ThumbnailUrl = p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                Shop = new ProductSearchShopDto
                {
                    Id = p.Shop.Id,
                    Name = p.Shop.Name
                },
                Category = p.Category != null
                    ? new ProductSearchCategoryDto
                    {
                        Id = p.Category.Id,
                        Name = p.Category.Name
                    }
                    : null,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<ProductSearchDto>
        {
            Items = items,
            Page = searchParams.Page,
            PageSize = searchParams.PageSize,
            TotalItems = totalCount
        };
    }

    public async Task<PagedResult<ShopSearchDto>> SearchShopsAsync(string? q, PaginationParams pagination)
    {
        if (pagination.Page < 1)
            throw new ArgumentOutOfRangeException(nameof(pagination.Page), "Page must be greater than or equal to 1.");

        if (pagination.PageSize < 1 || pagination.PageSize > 50)
            throw new ArgumentOutOfRangeException(nameof(pagination.PageSize), "PageSize must be between 1 and 50.");

        var query = _context.Shops
            .AsNoTracking()
            .Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmedQuery = q.Trim();
            if (trimmedQuery.Length > 100)
                throw new ArgumentException("Search query cannot exceed 100 characters.", nameof(q));

            var lowerQuery = trimmedQuery.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(lowerQuery) || (s.Description != null && s.Description.ToLower().Contains(lowerQuery)));
        }

        query = query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(s => new ShopSearchDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                LogoUrl = s.AvatarUrl,
                ProductCount = s.Products.Count(p => p.IsActive),
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<ShopSearchDto>
        {
            Items = items,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalCount
        };
    }

    public async Task<UnifiedSearchResponse> UnifiedSearchAsync(UnifiedSearchParams searchParams, Guid? currentUserId = null)
    {
        var lowerQuery = NormalizeQuery(searchParams.Q);
        var type = (searchParams.Type ?? "all").Trim().ToLowerInvariant();
        var sort = (searchParams.Sort ?? "relevance").Trim().ToLowerInvariant();
        var dateRange = (searchParams.DateRange ?? "all").Trim().ToLowerInvariant();
        var page = searchParams.Page < 1 ? 1 : searchParams.Page;
        var pageSize = searchParams.PageSize;
        var minDate = GetMinDate(dateRange);

        var isAll = type == "all";
        var effectivePage = isAll ? 1 : page;
        var effectivePageSize = isAll ? 5 : pageSize;

        var response = new UnifiedSearchResponse
        {
            Users = new PagedResult<UserSearchDto> { Page = 1, PageSize = effectivePageSize, TotalItems = 0 },
            Posts = new PagedResult<PostDto> { Page = 1, PageSize = effectivePageSize, TotalItems = 0 },
            Shorts = new PagedResult<PostDto> { Page = 1, PageSize = effectivePageSize, TotalItems = 0 },
            Products = new PagedResult<ProductSearchDto> { Page = 1, PageSize = effectivePageSize, TotalItems = 0 },
            Shops = new PagedResult<ShopSearchDto> { Page = 1, PageSize = effectivePageSize, TotalItems = 0 }
        };

        if (isAll || type == "users")
            response.Users = await QueryUsersAsync(lowerQuery, sort, minDate, effectivePage, effectivePageSize);

        if (isAll || type == "posts")
            response.Posts = await QueryPostsAsync(lowerQuery, sort, minDate, shortsOnly: false, effectivePage, effectivePageSize, currentUserId);

        if (isAll || type == "shorts")
            response.Shorts = await QueryPostsAsync(lowerQuery, sort, minDate, shortsOnly: true, effectivePage, effectivePageSize, currentUserId);

        if (isAll || type == "products")
            response.Products = await QueryProductsAsync(lowerQuery, sort, minDate, effectivePage, effectivePageSize);

        if (isAll || type == "shops")
            response.Shops = await QueryShopsAsync(lowerQuery, sort, minDate, effectivePage, effectivePageSize);

        return response;
    }

    public async Task<SearchSuggestionsDto> GetSuggestionsAsync(string? q, Guid? currentUserId = null)
    {
        var lowerQuery = NormalizeQuery(q);
        if (lowerQuery == null)
            return new SearchSuggestionsDto();

        var users = await QueryUsersAsync(lowerQuery, "relevance", null, 1, 3);
        var products = await QueryProductsAsync(lowerQuery, "relevance", null, 1, 3);
        var shops = await QueryShopsAsync(lowerQuery, "relevance", null, 1, 2);
        var posts = await QueryPostsAsync(lowerQuery, "relevance", null, shortsOnly: false, 1, 2, currentUserId);

        return new SearchSuggestionsDto
        {
            Users = users.Items,
            Products = products.Items,
            Shops = shops.Items,
            Posts = posts.Items
        };
    }

    private static string? NormalizeQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return null;

        var trimmed = q.Trim();
        if (trimmed.Length > 100)
            throw new ArgumentException("Search query cannot exceed 100 characters.", nameof(q));

        return trimmed.ToLower();
    }

    private static DateTime? GetMinDate(string dateRange)
    {
        return dateRange switch
        {
            "today" => DateTime.UtcNow.Date,
            "week" => DateTime.UtcNow.AddDays(-7),
            "month" => DateTime.UtcNow.AddDays(-30),
            _ => null
        };
    }

    private async Task<PagedResult<UserSearchDto>> QueryUsersAsync(
        string? lowerQuery, string sort, DateTime? minDate, int page, int pageSize)
    {
        var query = _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive);

        if (lowerQuery != null)
        {
            query = query.Where(u =>
                u.Username.ToLower().Contains(lowerQuery) ||
                u.DisplayName.ToLower().Contains(lowerQuery) ||
                (u.Bio != null && u.Bio.ToLower().Contains(lowerQuery)));
        }

        if (minDate.HasValue)
        {
            query = query.Where(u => u.CreatedAt >= minDate.Value);
        }

        IOrderedQueryable<Domain.Entities.User> ordered;
        if (sort == "recent")
        {
            ordered = query.OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id);
        }
        else if (lowerQuery != null)
        {
            ordered = query
                .OrderByDescending(u => u.Username.ToLower() == lowerQuery || u.DisplayName.ToLower() == lowerQuery)
                .ThenByDescending(u => u.Username.ToLower().StartsWith(lowerQuery) || u.DisplayName.ToLower().StartsWith(lowerQuery))
                .ThenByDescending(u => u.Followers.Count)
                .ThenByDescending(u => u.CreatedAt);
        }
        else
        {
            ordered = query
                .OrderByDescending(u => u.Followers.Count)
                .ThenByDescending(u => u.CreatedAt);
        }

        var totalItems = await ordered.CountAsync();
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserSearchDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Bio = u.Bio,
                Role = u.Role.ToString(),
                FollowersCount = u.Followers.Count
            })
            .ToListAsync();

        return new PagedResult<UserSearchDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    private async Task<PagedResult<PostDto>> QueryPostsAsync(
        string? lowerQuery, string sort, DateTime? minDate, bool shortsOnly, int page, int pageSize, Guid? currentUserId)
    {
        var query = _context.Posts
            .AsNoTracking()
            .Where(p => p.User.IsActive);

        if (shortsOnly)
        {
            query = query.Where(p => p.Media.Any(m => m.MediaType == MediaType.Video));
        }
        else
        {
            query = query.Where(p => !p.Media.Any(m => m.MediaType == MediaType.Video));
        }

        if (lowerQuery != null)
        {
            query = query.Where(p =>
                p.Content.ToLower().Contains(lowerQuery) ||
                p.User.Username.ToLower().Contains(lowerQuery) ||
                p.User.DisplayName.ToLower().Contains(lowerQuery));
        }

        if (minDate.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= minDate.Value);
        }

        IOrderedQueryable<Domain.Entities.Post> ordered;
        if (sort == "recent")
        {
            ordered = query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        }
        else
        {
            ordered = query
                .OrderByDescending(p => p.Reactions.Count + p.Comments.Count)
                .ThenByDescending(p => p.CreatedAt);
        }

        var totalItems = await ordered.CountAsync();
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PostDto
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
                CurrentUserReaction = currentUserId.HasValue
                    ? p.Reactions
                        .Where(r => r.UserId == currentUserId.Value)
                        .Select(r => r.ReactionType.ToString())
                        .FirstOrDefault()
                    : null,
                Media = p.Media.Select(m => new PostMediaDto
                {
                    Id = m.Id,
                    MediaType = m.MediaType.ToString(),
                    StorageUrl = m.StorageUrl,
                    ThumbnailUrl = m.ThumbnailUrl
                }).ToList(),
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        return new PagedResult<PostDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    private async Task<PagedResult<ProductSearchDto>> QueryProductsAsync(
        string? lowerQuery, string sort, DateTime? minDate, int page, int pageSize)
    {
        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.Shop.IsActive);

        if (lowerQuery != null)
        {
            query = query.Where(p =>
                p.Name.ToLower().Contains(lowerQuery) ||
                (p.Description != null && p.Description.ToLower().Contains(lowerQuery)));
        }

        if (minDate.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= minDate.Value);
        }

        IOrderedQueryable<Domain.Entities.Product> ordered;
        if (sort == "recent")
        {
            ordered = query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        }
        else if (lowerQuery != null)
        {
            ordered = query
                .OrderByDescending(p => p.Name.ToLower() == lowerQuery)
                .ThenByDescending(p => p.Name.ToLower().StartsWith(lowerQuery))
                .ThenByDescending(p => p.CreatedAt);
        }
        else
        {
            ordered = query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        }

        var totalItems = await ordered.CountAsync();
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductSearchDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                ThumbnailUrl = p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                Shop = new ProductSearchShopDto
                {
                    Id = p.Shop.Id,
                    Name = p.Shop.Name
                },
                Category = p.Category != null
                    ? new ProductSearchCategoryDto
                    {
                        Id = p.Category.Id,
                        Name = p.Category.Name
                    }
                    : null,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<ProductSearchDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    private async Task<PagedResult<ShopSearchDto>> QueryShopsAsync(
        string? lowerQuery, string sort, DateTime? minDate, int page, int pageSize)
    {
        var query = _context.Shops
            .AsNoTracking()
            .Where(s => s.IsActive);

        if (lowerQuery != null)
        {
            query = query.Where(s =>
                s.Name.ToLower().Contains(lowerQuery) ||
                (s.Description != null && s.Description.ToLower().Contains(lowerQuery)));
        }

        if (minDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= minDate.Value);
        }

        IOrderedQueryable<Domain.Entities.Shop> ordered;
        if (sort == "recent")
        {
            ordered = query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);
        }
        else if (lowerQuery != null)
        {
            ordered = query
                .OrderByDescending(s => s.Name.ToLower() == lowerQuery)
                .ThenByDescending(s => s.Name.ToLower().StartsWith(lowerQuery))
                .ThenByDescending(s => s.Products.Count(p => p.IsActive))
                .ThenByDescending(s => s.CreatedAt);
        }
        else
        {
            ordered = query
                .OrderByDescending(s => s.Products.Count(p => p.IsActive))
                .ThenByDescending(s => s.CreatedAt);
        }

        var totalItems = await ordered.CountAsync();
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ShopSearchDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                LogoUrl = s.AvatarUrl,
                ProductCount = s.Products.Count(p => p.IsActive),
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<ShopSearchDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }
}
