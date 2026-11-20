using MySocialApp.Application.DTOs.Revenue;

namespace MySocialApp.Application.Interfaces;

public interface IRevenueService
{
    Task<RevenueDto> GetShopRevenueAsync(Guid ownerId, DateTime from, DateTime to);
    Task<RevenueDto> GetShipperRevenueAsync(Guid shipperId, DateTime from, DateTime to);
    Task<RevenueDto> GetTotalRevenueAsync(DateTime from, DateTime to);
}
