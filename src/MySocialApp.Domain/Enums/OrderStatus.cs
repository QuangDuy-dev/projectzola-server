namespace MySocialApp.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Preparing = 2,
    WaitingForPickup = 3,
    Shipping = 4,
    Delivered = 5,
    Cancelled = 6
}
