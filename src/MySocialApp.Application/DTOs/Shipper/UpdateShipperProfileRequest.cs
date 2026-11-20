using System.ComponentModel.DataAnnotations;

namespace MySocialApp.Application.DTOs.Shipper;

public class UpdateShipperProfileRequest
{
    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(50)]
    public string? VehicleType { get; set; }

    [StringLength(20)]
    public string? VehiclePlate { get; set; }

    public bool? IsAvailable { get; set; }
}
