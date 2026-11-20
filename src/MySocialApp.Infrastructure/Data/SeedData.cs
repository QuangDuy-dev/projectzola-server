using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Infrastructure.Data;

public static class SeedData
{
    public static void Initialize(MySocialAppDbContext context)
    {
        if (context.Users.Any()) return;

        var hasher = new PasswordHasher<User>();

        // Create users
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            Email = "admin@mysocialapp.com",
            DisplayName = "Administrator",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        admin.PasswordHash = hasher.HashPassword(admin, "Admin@123");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "user1",
            Email = "user1@mysocialapp.com",
            DisplayName = "Regular User",
            Bio = "Just a regular user",
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "User@123");

        var shopOwner = new User
        {
            Id = Guid.NewGuid(),
            Username = "shopowner1",
            Email = "shop@mysocialapp.com",
            DisplayName = "Shop Owner",
            Role = UserRole.ShopOwner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        shopOwner.PasswordHash = hasher.HashPassword(shopOwner, "Shop@123");

        var shipper = new User
        {
            Id = Guid.NewGuid(),
            Username = "shipper1",
            Email = "shipper@mysocialapp.com",
            DisplayName = "Shipper",
            Role = UserRole.Shipper,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        shipper.PasswordHash = hasher.HashPassword(shipper, "Shipper@123");

        context.Users.AddRange(admin, user, shopOwner, shipper);

        // Categories
        var electronics = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Electronics",
            Description = "Electronic devices and accessories",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var phones = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Phones",
            Description = "Smartphones and accessories",
            ParentCategoryId = electronics.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var fashion = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Fashion",
            Description = "Clothing and accessories",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Categories.AddRange(electronics, phones, fashion);

        // Shop
        var shop = new Shop
        {
            Id = Guid.NewGuid(),
            OwnerId = shopOwner.Id,
            Name = "Tech Store",
            Description = "Your favorite tech store",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Shops.Add(shop);

        // Products
        var product1 = new Product
        {
            Id = Guid.NewGuid(),
            ShopId = shop.Id,
            Name = "Sample Phone",
            Description = "A great smartphone",
            Price = 999.99m,
            Stock = 50,
            Sku = "PHONE-001",
            CategoryId = phones.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var product2 = new Product
        {
            Id = Guid.NewGuid(),
            ShopId = shop.Id,
            Name = "Sample T-Shirt",
            Description = "A cool t-shirt",
            Price = 29.99m,
            Stock = 100,
            Sku = "FASHION-001",
            CategoryId = fashion.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Products.AddRange(product1, product2);

        // Posts
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Content = "Hello everyone! This is my first post on MySocialApp!",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Posts.Add(post);

        // Comment
        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            PostId = post.Id,
            UserId = shopOwner.Id,
            Content = "Welcome to the platform!",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Comments.Add(comment);

        // ShipperProfile
        var shipperProfile = new ShipperProfile
        {
            Id = Guid.NewGuid(),
            UserId = shipper.Id,
            Phone = "0901234567",
            VehicleType = "Motorcycle",
            VehiclePlate = "59-X1 12345",
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.ShipperProfiles.Add(shipperProfile);

        context.SaveChanges();
    }
}
