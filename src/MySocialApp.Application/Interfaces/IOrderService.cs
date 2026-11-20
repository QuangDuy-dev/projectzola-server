using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Orders;

namespace MySocialApp.Application.Interfaces;

public interface IOrderService
{
    Task<List<OrderDto>> CreateAsync(Guid buyerId, CreateOrderRequest request);
    Task<PagedResult<OrderDto>> GetBuyerOrdersAsync(Guid buyerId, PaginationParams pagination, string? status = null);
    Task<OrderDto> GetByIdAsync(Guid orderId, Guid userId);
    Task CancelAsync(Guid orderId, Guid buyerId);
    Task<PagedResult<OrderDto>> GetShopOrdersAsync(Guid ownerId, PaginationParams pagination, string? status = null);
    Task ConfirmAsync(Guid orderId, Guid ownerId);
    Task PrepareAsync(Guid orderId, Guid ownerId);
    Task ReadyForPickupAsync(Guid orderId, Guid ownerId);
    Task<PagedResult<OrderDto>> GetAvailableForShipperAsync(PaginationParams pagination);
    Task ClaimAsync(Guid orderId, Guid shipperId);
    Task PickupAsync(Guid orderId, Guid shipperId);
    Task DeliverAsync(Guid orderId, Guid shipperId);
    Task<PagedResult<OrderDto>> GetShipperOrdersAsync(Guid shipperId, PaginationParams pagination, string? status = null);
    Task<PagedResult<OrderDto>> GetAllOrdersAsync(PaginationParams pagination, string? status = null);
}
