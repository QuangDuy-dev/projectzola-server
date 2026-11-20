using MySocialApp.Application.DTOs.Shipper;

namespace MySocialApp.Application.Interfaces;

public interface IShipperService
{
    Task<ShipperProfileDto?> GetProfileAsync(Guid userId);
    Task<ShipperProfileDto> UpdateProfileAsync(Guid userId, UpdateShipperProfileRequest request);
}
