using MySocialApp.Application.DTOs.Admin;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Posts;
using MySocialApp.Application.DTOs.Shops;
using MySocialApp.Application.DTOs.Users;

namespace MySocialApp.Application.Interfaces;

public interface IAdminService
{
    Task<PagedResult<UserDto>> GetAllUsersAsync(PaginationParams pagination, string? q = null, string? role = null, bool? isActive = null);
    Task<UserDto> GetUserByIdAsync(Guid id);
    Task<AdminUserDetailDto> GetUserDetailAsync(Guid id);
    Task<UserDto> CreateUserAsync(AdminCreateUserRequest request);
    Task<UserDto> UpdateUserAsync(Guid id, AdminUpdateUserRequest request);
    Task DeleteUserAsync(Guid id, bool permanent = false, Guid? currentAdminId = null);
    Task<UserDto> ToggleUserActiveAsync(Guid id, Guid? currentAdminId = null);

    Task<PagedResult<PostDto>> GetAllPostsAsync(PaginationParams pagination);
    Task DeletePostAsync(Guid id);

    Task<PagedResult<ShopDto>> GetAllShopsAsync(PaginationParams pagination);
    Task<ShopDto> ToggleShopActiveAsync(Guid id);
}
