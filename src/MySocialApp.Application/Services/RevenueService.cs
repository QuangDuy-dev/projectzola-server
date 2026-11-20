using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Revenue;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Application.Services;

public class RevenueService : IRevenueService
{
    private readonly IApplicationDbContext _context;

    public RevenueService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RevenueDto> GetShopRevenueAsync(Guid ownerId, DateTime from, DateTime to)
    {
        var shopIds = await _context.Shops
            .Where(s => s.OwnerId == ownerId)
            .Select(s => s.Id)
            .ToListAsync();

        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => shopIds.Contains(o.ShopId)
                && o.Status == OrderStatus.Delivered
                && o.DeliveredAt != null
                && o.DeliveredAt.Value >= from && o.DeliveredAt.Value <= to)
            .ToListAsync();

        return new RevenueDto
        {
            TotalRevenue = orders.Sum(o => o.TotalPrice),
            TotalOrders = orders.Count,
            TotalItemsSold = orders.SelectMany(o => o.Items).Sum(i => i.Quantity),
            TotalShippingFees = orders.Sum(o => o.ShippingFee),
            From = from,
            To = to
        };
    }

    public async Task<RevenueDto> GetShipperRevenueAsync(Guid shipperId, DateTime from, DateTime to)
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.ShipperId == shipperId
                && o.Status == OrderStatus.Delivered
                && o.DeliveredAt != null
                && o.DeliveredAt.Value >= from && o.DeliveredAt.Value <= to)
            .ToListAsync();

        return new RevenueDto
        {
            TotalRevenue = orders.Sum(o => o.ShippingFee),
            TotalOrders = orders.Count,
            TotalItemsSold = orders.SelectMany(o => o.Items).Sum(i => i.Quantity),
            TotalShippingFees = orders.Sum(o => o.ShippingFee),
            From = from,
            To = to
        };
    }

    public async Task<RevenueDto> GetTotalRevenueAsync(DateTime from, DateTime to)
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Delivered
                && o.DeliveredAt != null
                && o.DeliveredAt.Value >= from && o.DeliveredAt.Value <= to)
            .ToListAsync();

        return new RevenueDto
        {
            TotalRevenue = orders.Sum(o => o.TotalPrice),
            TotalOrders = orders.Count,
            TotalItemsSold = orders.SelectMany(o => o.Items).Sum(i => i.Quantity),
            TotalShippingFees = orders.Sum(o => o.ShippingFee),
            From = from,
            To = to
        };
    }
}
