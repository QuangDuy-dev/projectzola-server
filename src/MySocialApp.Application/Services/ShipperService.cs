using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Shipper;
using MySocialApp.Application.Interfaces;
using MySocialApp.Domain.Entities;

namespace MySocialApp.Application.Services;

public class ShipperService : IShipperService
{
    private readonly IApplicationDbContext _context;

    public ShipperService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ShipperProfileDto?> GetProfileAsync(Guid userId)
    {
        var profile = await _context.ShipperProfiles
            .FirstOrDefaultAsync(sp => sp.UserId == userId);

        if (profile == null) return null;

        return new ShipperProfileDto
        {
            Id = profile.Id,
            UserId = profile.UserId,
            Phone = profile.Phone,
            VehicleType = profile.VehicleType,
            VehiclePlate = profile.VehiclePlate,
            IsAvailable = profile.IsAvailable,
            CreatedAt = profile.CreatedAt
        };
    }

    public async Task<ShipperProfileDto> UpdateProfileAsync(Guid userId, UpdateShipperProfileRequest request)
    {
        var profile = await _context.ShipperProfiles
            .FirstOrDefaultAsync(sp => sp.UserId == userId);

        if (profile == null)
        {
            profile = new ShipperProfile { UserId = userId };
            _context.ShipperProfiles.Add(profile);
        }

        if (request.Phone != null) profile.Phone = request.Phone;
        if (request.VehicleType != null) profile.VehicleType = request.VehicleType;
        if (request.VehiclePlate != null) profile.VehiclePlate = request.VehiclePlate;
        if (request.IsAvailable.HasValue) profile.IsAvailable = request.IsAvailable.Value;

        await _context.SaveChangesAsync();

        return new ShipperProfileDto
        {
            Id = profile.Id,
            UserId = profile.UserId,
            Phone = profile.Phone,
            VehicleType = profile.VehicleType,
            VehiclePlate = profile.VehiclePlate,
            IsAvailable = profile.IsAvailable,
            CreatedAt = profile.CreatedAt
        };
    }
}
