namespace MySocialApp.Domain.Enums;

public enum NotificationType
{
    NewFollower = 0,
    PostLiked = 1,
    PostCommented = 2,
    OrderConfirmed = 3,
    OrderPreparing = 4,
    OrderReadyForPickup = 5,
    OrderClaimed = 6,
    OrderShipping = 7,
    OrderDelivered = 8,
    OrderCancelled = 9,
    SystemAnnouncement = 10
}
