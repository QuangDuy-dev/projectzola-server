using MySocialApp.Application.DTOs.Cart;

namespace MySocialApp.Application.Interfaces;

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid userId);
    Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request);
    Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request);
    Task RemoveItemAsync(Guid userId, Guid itemId);
    Task ClearCartAsync(Guid userId);
}
