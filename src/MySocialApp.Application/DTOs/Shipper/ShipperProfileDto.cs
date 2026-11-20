namespace MySocialApp.Application.DTOs.Shipper;

public class ShipperProfileDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Phone { get; set; }
    public string? VehicleType { get; set; }
    public string? VehiclePlate { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; }
}
