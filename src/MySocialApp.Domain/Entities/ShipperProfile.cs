using MySocialApp.Domain.Common;

namespace MySocialApp.Domain.Entities;

public class ShipperProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string? Phone { get; set; }
    public string? VehicleType { get; set; }
    public string? VehiclePlate { get; set; }
    public bool IsAvailable { get; set; } = true;
}
