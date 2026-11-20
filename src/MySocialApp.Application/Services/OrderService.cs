using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class OrderService : IOrderService
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationService? _notificationService;

    public OrderService(
        IApplicationDbContext context,
        INotificationService? notificationService = null)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<List<OrderDto>> CreateAsync(Guid buyerId, CreateOrderRequest request)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Shop)
            .FirstOrDefaultAsync(c => c.UserId == buyerId)
            ?? throw new InvalidOperationException("Cart is empty.");

        if (!cart.Items.Any())
            throw new InvalidOperationException("Cart is empty.");

        // Group items by shop to create one Order per Shop
        var groupedByShop = cart.Items.GroupBy(i => i.Product.ShopId).ToList();
        var orders = new List<Order>();

        foreach (var group in groupedByShop)
        {
            var order = new Order
            {
                BuyerId = buyerId,
                ShopId = group.Key,
                ShippingAddress = request.ShippingAddress,
                Status = OrderStatus.Pending,
                ShippingFee = 0
            };

            foreach (var cartItem in group)
            {
                if (!cartItem.Product.Shop.IsActive)
                    throw new InvalidOperationException($"Shop '{cartItem.Product.Shop.Name}' is not active.");

                if (!cartItem.Product.IsActive)
                    throw new InvalidOperationException($"Product '{cartItem.Product.Name}' is no longer active.");

                if (cartItem.Quantity <= 0)
                    throw new InvalidOperationException("Quantity must be greater than zero.");

                if (cartItem.Product.Stock <= 0 || cartItem.Quantity > cartItem.Product.Stock)
                    throw new InvalidOperationException($"Insufficient stock for {cartItem.Product.Name}.");

                order.Items.Add(new OrderItem
                {
                    ProductId = cartItem.ProductId,
                    ProductNameSnapshot = cartItem.Product.Name,
                    UnitPrice = cartItem.Product.Price,
                    Quantity = cartItem.Quantity,
                    Subtotal = cartItem.Product.Price * cartItem.Quantity
                });

                // Deduct authoritative stock
                cartItem.Product.Stock -= cartItem.Quantity;
            }

            order.TotalPrice = order.Items.Sum(i => i.Subtotal);
            orders.Add(order);
        }

        _context.Orders.AddRange(orders);
        _context.CartItems.RemoveRange(cart.Items);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Product stock was modified by another concurrent transaction. Please refresh your cart and try again.");
        }

        // Return all created orders
        var orderDtos = new List<OrderDto>();
        foreach (var order in orders)
        {
            orderDtos.Add(await GetByIdAsync(order.Id, buyerId));
        }

        return orderDtos;
    }

    public async Task<PagedResult<OrderDto>> GetBuyerOrdersAsync(Guid buyerId, PaginationParams pagination, string? status = null)
    {
        var query = _context.Orders.Where(o => o.BuyerId == buyerId);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
        {
            query = query.Where(o => o.Status == s);
        }

        return await GetOrdersPagedAsync(query, pagination);
    }

    public async Task<OrderDto> GetByIdAsync(Guid orderId, Guid userId)
    {
        var order = await _context.Orders
            .Include(o => o.Buyer)
            .Include(o => o.Shop)
            .Include(o => o.Shipper)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException("Order not found.");

        var isBuyer = order.BuyerId == userId;
        var isShopOwner = order.Shop.OwnerId == userId;
        var isShipper = order.ShipperId == userId;

        if (!isBuyer && !isShopOwner && !isShipper)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Role != UserRole.Admin)
            {
                throw new UnauthorizedAccessException("Not authorized to view this order.");
            }
        }

        return MapToDto(order);
    }

    public async Task CancelAsync(Guid orderId, Guid userId)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status == OrderStatus.Delivered)
            throw new InvalidOperationException("Delivered orders cannot be cancelled.");

        if (order.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled.");

        var shop = await _context.Shops.FindAsync(order.ShopId);
        bool isBuyer = order.BuyerId == userId;
        bool isShopOwner = shop != null && shop.OwnerId == userId;
        bool isAdmin = false;

        if (!isBuyer && !isShopOwner)
        {
            var user = await _context.Users.FindAsync(userId);
            isAdmin = user?.Role == UserRole.Admin;
        }

        if (!isBuyer && !isShopOwner && !isAdmin)
            throw new UnauthorizedAccessException("Not authorized to cancel this order.");

        // Buyer can only cancel Pending orders
        if (isBuyer && !isShopOwner && !isAdmin && order.Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException("Customers can only cancel orders in Pending status.");
        }

        ValidateTransition(order.Status, OrderStatus.Cancelled);

        // Restore stock for all items
        foreach (var item in order.Items)
        {
            item.Product.Stock += item.Quantity;
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (_notificationService != null)
        {
            if (isBuyer && shop != null)
            {
                await _notificationService.CreateAsync(
                    shop.OwnerId,
                    NotificationType.OrderCancelled,
                    "Order Cancelled",
                    "The buyer has cancelled their order.",
                    order.Id);
            }
            else if (isShopOwner || isAdmin)
            {
                await _notificationService.CreateAsync(
                    order.BuyerId,
                    NotificationType.OrderCancelled,
                    "Order Cancelled",
                    "The shop has cancelled your order.",
                    order.Id);
            }
        }
    }

    public async Task<PagedResult<OrderDto>> GetShopOrdersAsync(Guid ownerId, PaginationParams pagination, string? status = null)
    {
        var shopIds = await _context.Shops
            .Where(s => s.OwnerId == ownerId)
            .Select(s => s.Id)
            .ToListAsync();

        var query = _context.Orders.Where(o => shopIds.Contains(o.ShopId));
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
        {
            query = query.Where(o => o.Status == s);
        }

        return await GetOrdersPagedAsync(query, pagination);
    }

    public async Task ConfirmAsync(Guid orderId, Guid ownerId)
        => await UpdateStatusByOwner(orderId, ownerId, OrderStatus.Confirmed);

    public async Task PrepareAsync(Guid orderId, Guid ownerId)
        => await UpdateStatusByOwner(orderId, ownerId, OrderStatus.Preparing);

    public async Task ReadyForPickupAsync(Guid orderId, Guid ownerId)
        => await UpdateStatusByOwner(orderId, ownerId, OrderStatus.WaitingForPickup);

    public async Task<PagedResult<OrderDto>> GetAvailableForShipperAsync(PaginationParams pagination)
    {
        return await GetOrdersPagedAsync(
            _context.Orders.Where(o => o.Status == OrderStatus.WaitingForPickup && o.ShipperId == null),
            pagination);
    }

    public async Task ClaimAsync(Guid orderId, Guid shipperId)
    {
        var order = await GetOrderAsync(orderId);
        if (order.Status != OrderStatus.WaitingForPickup)
            throw new InvalidOperationException("Order is not available for pickup.");
        if (order.ShipperId != null)
            throw new InvalidOperationException("Order already claimed.");

        order.ShipperId = shipperId;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Order already claimed by another shipper.");
        }

        if (_notificationService != null)
        {
            var shop = await _context.Shops.FindAsync(order.ShopId);
            await _notificationService.CreateAsync(
                order.BuyerId,
                NotificationType.OrderClaimed,
                "Order Claimed",
                "A shipper has claimed your order.",
                order.Id);

            if (shop != null && shop.OwnerId != Guid.Empty)
            {
                await _notificationService.CreateAsync(
                    shop.OwnerId,
                    NotificationType.OrderClaimed,
                    "Order Claimed",
                    "A shipper has claimed the order for delivery.",
                    order.Id);
            }
        }
    }

    public async Task PickupAsync(Guid orderId, Guid shipperId)
    {
        var order = await GetOrderAsync(orderId);
        if (order.ShipperId != shipperId)
            throw new UnauthorizedAccessException("Not authorized.");
        ValidateTransition(order.Status, OrderStatus.Shipping);
        order.Status = OrderStatus.Shipping;
        await _context.SaveChangesAsync();

        if (_notificationService != null)
        {
            await _notificationService.CreateAsync(
                order.BuyerId,
                NotificationType.OrderShipping,
                "Order Shipping",
                "Your order is on the way.",
                order.Id);
        }
    }

    public async Task DeliverAsync(Guid orderId, Guid shipperId)
    {
        var order = await GetOrderAsync(orderId);
        if (order.ShipperId != shipperId)
            throw new UnauthorizedAccessException("Not authorized.");
        ValidateTransition(order.Status, OrderStatus.Delivered);
        order.Status = OrderStatus.Delivered;
        order.DeliveredAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (_notificationService != null)
        {
            var shop = await _context.Shops.FindAsync(order.ShopId);
            await _notificationService.CreateAsync(
                order.BuyerId,
                NotificationType.OrderDelivered,
                "Order Delivered",
                "Your order has been delivered successfully.",
                order.Id);

            if (shop != null && shop.OwnerId != Guid.Empty)
            {
                await _notificationService.CreateAsync(
                    shop.OwnerId,
                    NotificationType.OrderDelivered,
                    "Order Delivered",
                    "The order has been delivered to the buyer.",
                    order.Id);
            }
        }
    }

    public async Task<PagedResult<OrderDto>> GetShipperOrdersAsync(Guid shipperId, PaginationParams pagination, string? status = null)
    {
        var query = _context.Orders.Where(o => o.ShipperId == shipperId);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
        {
            query = query.Where(o => o.Status == s);
        }

        return await GetOrdersPagedAsync(query, pagination);
    }

    public async Task<PagedResult<OrderDto>> GetAllOrdersAsync(PaginationParams pagination, string? status = null)
    {
        var query = _context.Orders.AsQueryable();
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
            query = query.Where(o => o.Status == s);

        return await GetOrdersPagedAsync(query, pagination);
    }

    private async Task UpdateStatusByOwner(Guid orderId, Guid ownerId, OrderStatus newStatus)
    {
        var order = await GetOrderAsync(orderId);
        var shop = await _context.Shops.FindAsync(order.ShopId)
            ?? throw new KeyNotFoundException("Shop not found.");

        if (shop.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Not authorized.");

        ValidateTransition(order.Status, newStatus);
        order.Status = newStatus;
        await _context.SaveChangesAsync();

        if (_notificationService != null)
        {
            var notifType = newStatus switch
            {
                OrderStatus.Confirmed => NotificationType.OrderConfirmed,
                OrderStatus.Preparing => NotificationType.OrderPreparing,
                OrderStatus.WaitingForPickup => NotificationType.OrderReadyForPickup,
                _ => (NotificationType?)null
            };

            if (notifType.HasValue)
            {
                var (title, content) = notifType.Value switch
                {
                    NotificationType.OrderConfirmed => ("Order Confirmed", "Your order has been confirmed by the shop."),
                    NotificationType.OrderPreparing => ("Order Preparing", "Your order is being prepared."),
                    NotificationType.OrderReadyForPickup => ("Order Ready for Pickup", "Your order is ready for shipper pickup."),
                    _ => ("Order Updated", "Your order status has been updated.")
                };

                await _notificationService.CreateAsync(order.BuyerId, notifType.Value, title, content, order.Id);
            }
        }
    }

    private async Task<Order> GetOrderAsync(Guid orderId)
    {
        return await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException("Order not found.");
    }

    private static void ValidateTransition(OrderStatus current, OrderStatus target)
    {
        var validTransitions = new Dictionary<OrderStatus, OrderStatus[]>
        {
            { OrderStatus.Pending, new[] { OrderStatus.Confirmed, OrderStatus.Cancelled } },
            { OrderStatus.Confirmed, new[] { OrderStatus.Preparing, OrderStatus.Cancelled } },
            { OrderStatus.Preparing, new[] { OrderStatus.WaitingForPickup, OrderStatus.Cancelled } },
            { OrderStatus.WaitingForPickup, new[] { OrderStatus.Shipping, OrderStatus.Cancelled } },
            { OrderStatus.Shipping, new[] { OrderStatus.Delivered } }
        };

        if (!validTransitions.ContainsKey(current) || !validTransitions[current].Contains(target))
            throw new InvalidOperationException($"Cannot transition from {current} to {target}.");
    }

    private static async Task<PagedResult<OrderDto>> GetOrdersPagedAsync(
        IQueryable<Order> query, PaginationParams pagination)
    {
        var orderedQuery = query
            .Include(o => o.Buyer)
            .Include(o => o.Shop)
            .Include(o => o.Shipper)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt);

        var totalItems = await orderedQuery.CountAsync();
        var orders = await orderedQuery
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        return new PagedResult<OrderDto>
        {
            Items = orders.Select(MapToDto).ToList(),
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalItems = totalItems
        };
    }

    private static OrderDto MapToDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            BuyerId = order.BuyerId,
            BuyerName = order.Buyer.DisplayName,
            ShopId = order.ShopId,
            ShopName = order.Shop.Name,
            ShipperId = order.ShipperId,
            ShipperName = order.Shipper?.DisplayName,
            TotalPrice = order.TotalPrice,
            ShippingFee = order.ShippingFee,
            Status = order.Status.ToString(),
            ShippingAddress = order.ShippingAddress,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductNameSnapshot = i.ProductNameSnapshot,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                Subtotal = i.Subtotal
            }).ToList(),
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            DeliveredAt = order.DeliveredAt,
            CancelledAt = order.CancelledAt
        };
    }
}
