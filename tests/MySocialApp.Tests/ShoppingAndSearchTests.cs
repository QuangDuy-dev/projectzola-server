using Microsoft.EntityFrameworkCore;
using MySocialApp.Application.DTOs.Cart;
using MySocialApp.Application.DTOs.Common;
using MySocialApp.Application.DTOs.Notifications;
using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.DTOs.Search;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Tests;

public class ShoppingAndSearchTests
{
    // ==========================================
    // 1. PRODUCT & SHOP SEARCH TESTS
    // ==========================================

    [Fact]
    public async Task SearchProducts_ByKeyword_ReturnsMatchingActiveProducts()
    {
        using var context = TestDbContextFactory.Create(nameof(SearchProducts_ByKeyword_ReturnsMatchingActiveProducts));

        var owner = new User { Id = Guid.NewGuid(), Username = "owner", Email = "o@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Tech Store", IsActive = true };
        var category = new Category { Id = Guid.NewGuid(), Name = "Electronics" };

        var p1 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "Wireless Mouse", Description = "Ergonomic mouse", Price = 25m, Stock = 10, IsActive = true };
        var p2 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "Gaming Keyboard", Description = "Mechanical keyboard", Price = 75m, Stock = 5, IsActive = true };
        var p3 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "Mouse Pad", Description = "Large mouse mat", Price = 15m, Stock = 20, IsActive = true };
        var pInactive = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "Old Mouse", Description = "Broken", Price = 5m, Stock = 0, IsActive = false };

        context.Users.Add(owner);
        context.Shops.Add(shop);
        context.Categories.Add(category);
        context.Products.AddRange(p1, p2, p3, pInactive);
        await context.SaveChangesAsync();

        var searchService = new SearchService(context);

        // Search for "  mouse  " (tests trimming and case insensitivity)
        var result = await searchService.SearchProductsAsync(new ProductSearchParams
        {
            Q = "  mouse  ",
            Page = 1,
            PageSize = 10
        });

        Assert.Equal(2, result.TotalItems);
        Assert.Contains(result.Items, p => p.Id == p1.Id);
        Assert.Contains(result.Items, p => p.Id == p3.Id);
        Assert.DoesNotContain(result.Items, p => p.Id == pInactive.Id);
    }

    [Fact]
    public async Task SearchProducts_FiltersAndSorts_Correctly()
    {
        using var context = TestDbContextFactory.Create(nameof(SearchProducts_FiltersAndSorts_Correctly));

        var owner = new User { Id = Guid.NewGuid(), Username = "owner2", Email = "o2@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shopA = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop A", IsActive = true };
        var shopB = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop B", IsActive = true };
        var catA = new Category { Id = Guid.NewGuid(), Name = "Cat A" };
        var catB = new Category { Id = Guid.NewGuid(), Name = "Cat B" };

        var p1 = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, CategoryId = catA.Id, Name = "Alpha", Price = 10m, Stock = 5, IsActive = true, CreatedAt = DateTime.UtcNow.AddMinutes(-30) };
        var p2 = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, CategoryId = catA.Id, Name = "Beta", Price = 50m, Stock = 5, IsActive = true, CreatedAt = DateTime.UtcNow.AddMinutes(-20) };
        var p3 = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, CategoryId = catA.Id, Name = "Gamma", Price = 100m, Stock = 5, IsActive = true, CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
        var p4 = new Product { Id = Guid.NewGuid(), ShopId = shopB.Id, CategoryId = catB.Id, Name = "Delta", Price = 50m, Stock = 5, IsActive = true, CreatedAt = DateTime.UtcNow };

        context.Users.Add(owner);
        context.Shops.AddRange(shopA, shopB);
        context.Categories.AddRange(catA, catB);
        context.Products.AddRange(p1, p2, p3, p4);
        await context.SaveChangesAsync();

        var searchService = new SearchService(context);

        // Filter by shopId, categoryId, price range: [20, 80]
        var filteredResult = await searchService.SearchProductsAsync(new ProductSearchParams
        {
            ShopId = shopA.Id,
            CategoryId = catA.Id,
            MinPrice = 20m,
            MaxPrice = 80m
        });

        Assert.Single(filteredResult.Items);
        Assert.Equal("Beta", filteredResult.Items[0].Name);

        // Sort by price_desc
        var descResult = await searchService.SearchProductsAsync(new ProductSearchParams
        {
            ShopId = shopA.Id,
            Sort = "price_desc"
        });
        Assert.Equal("Gamma", descResult.Items[0].Name);
        Assert.Equal("Beta", descResult.Items[1].Name);
        Assert.Equal("Alpha", descResult.Items[2].Name);

        // Sort by price_asc
        var ascResult = await searchService.SearchProductsAsync(new ProductSearchParams
        {
            ShopId = shopA.Id,
            Sort = "price_asc"
        });
        Assert.Equal("Alpha", ascResult.Items[0].Name);
        Assert.Equal("Beta", ascResult.Items[1].Name);
        Assert.Equal("Gamma", ascResult.Items[2].Name);

        // Sort by name
        var nameResult = await searchService.SearchProductsAsync(new ProductSearchParams
        {
            ShopId = shopA.Id,
            Sort = "name"
        });
        Assert.Equal("Alpha", nameResult.Items[0].Name);
        Assert.Equal("Beta", nameResult.Items[1].Name);
        Assert.Equal("Gamma", nameResult.Items[2].Name);
    }

    [Fact]
    public async Task SearchProducts_EnforcesMaxPageSize_50()
    {
        using var context = TestDbContextFactory.Create(nameof(SearchProducts_EnforcesMaxPageSize_50));

        var owner = new User { Id = Guid.NewGuid(), Username = "owner3", Email = "o3@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop 3", IsActive = true };

        context.Users.Add(owner);
        context.Shops.Add(shop);
        for (int i = 0; i < 60; i++)
        {
            context.Products.Add(new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = $"Product {i}", Price = 10m, Stock = 5, IsActive = true });
        }
        await context.SaveChangesAsync();

        var searchService = new SearchService(context);

        var result = await searchService.SearchProductsAsync(new ProductSearchParams
        {
            Page = 1,
            PageSize = 100 // Exceeds 50
        });

        Assert.Equal(50, result.PageSize);
        Assert.Equal(50, result.Items.Count);
        Assert.Equal(60, result.TotalItems);
    }

    [Fact]
    public async Task SearchShops_ReturnsActiveShopsWithProductCounts()
    {
        using var context = TestDbContextFactory.Create(nameof(SearchShops_ReturnsActiveShopsWithProductCounts));

        var owner = new User { Id = Guid.NewGuid(), Username = "owner4", Email = "o4@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var activeShop1 = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Bakery Delights", Description = "Fresh bread and pastry", IsActive = true };
        var activeShop2 = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Golden Bakery", Description = "Bakes and cakes", IsActive = true };
        var inactiveShop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Old Bakery", Description = "Closed", IsActive = false };

        context.Users.Add(owner);
        context.Shops.AddRange(activeShop1, activeShop2, inactiveShop);

        context.Products.Add(new Product { Id = Guid.NewGuid(), ShopId = activeShop1.Id, Name = "Baguette", Price = 2m, Stock = 10, IsActive = true });
        context.Products.Add(new Product { Id = Guid.NewGuid(), ShopId = activeShop1.Id, Name = "Croissant", Price = 3m, Stock = 10, IsActive = true });
        context.Products.Add(new Product { Id = Guid.NewGuid(), ShopId = activeShop1.Id, Name = "Old Dough", Price = 1m, Stock = 0, IsActive = false }); // inactive product shouldn't count in active products
        await context.SaveChangesAsync();

        var searchService = new SearchService(context);

        var result = await searchService.SearchShopsAsync("bakery", new PaginationParams { Page = 1, PageSize = 10 });

        Assert.Equal(2, result.TotalItems);
        var found1 = result.Items.FirstOrDefault(s => s.Id == activeShop1.Id);
        Assert.NotNull(found1);
        Assert.Equal(2, found1.ProductCount); // 2 active products
        Assert.DoesNotContain(result.Items, s => s.Id == inactiveShop.Id);
    }

    // ==========================================
    // 2. CART OPERATION TESTS
    // ==========================================

    [Fact]
    public async Task CartService_AddDuplicate_IncrementsQuantity()
    {
        using var context = TestDbContextFactory.Create(nameof(CartService_AddDuplicate_IncrementsQuantity));

        var user = new User { Id = Guid.NewGuid(), Username = "shopper1", Email = "s1@s.com", DisplayName = "Shopper" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner5", Email = "o5@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop 5", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Headphones", Price = 49.99m, Stock = 10, IsActive = true };

        context.Users.AddRange(user, owner);
        context.Shops.Add(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);

        // Add 2 items
        await cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 2 });

        // Add 3 more items
        var cart = await cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 3 });

        Assert.Single(cart.Items);
        Assert.Equal(5, cart.Items[0].Quantity);
        Assert.Equal(49.99m, cart.Items[0].UnitPrice);
        Assert.Equal(10, cart.Items[0].CurrentStock);
        Assert.True(cart.Items[0].IsAvailable);
        Assert.Equal(shop.Id, cart.Items[0].ShopId);
        Assert.Equal(shop.Name, cart.Items[0].ShopName);
        Assert.Equal(249.95m, cart.CartTotal);
        Assert.Single(cart.ShopGroups);

        // Ensure stock in DB was NOT decremented upon adding to cart
        var dbProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(10, dbProduct!.Stock);
    }

    [Fact]
    public async Task CartService_Rejects_InvalidQuantity_Or_ExceedingStock()
    {
        using var context = TestDbContextFactory.Create(nameof(CartService_Rejects_InvalidQuantity_Or_ExceedingStock));

        var user = new User { Id = Guid.NewGuid(), Username = "shopper2", Email = "s2@s.com", DisplayName = "Shopper" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner6", Email = "o6@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop 6", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Monitor", Price = 150m, Stock = 3, IsActive = true };

        context.Users.AddRange(user, owner);
        context.Shops.Add(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);

        // Reject 0 or negative
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = -2 }));

        // Reject exceeding available stock (wants 4, stock 3)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 4 }));
        Assert.Contains("Insufficient stock", ex.Message);

        // Add 2 items successfully
        await cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 2 });

        // Adding 2 more would exceed stock (2 existing + 2 new = 4 > 3)
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 2 }));
        Assert.Contains("Insufficient stock", ex2.Message);
    }

    [Fact]
    public async Task CartService_UpdateItem_RemoveItem_ClearCart()
    {
        using var context = TestDbContextFactory.Create(nameof(CartService_UpdateItem_RemoveItem_ClearCart));

        var user = new User { Id = Guid.NewGuid(), Username = "shopper3", Email = "s3@s.com", DisplayName = "Shopper" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner7", Email = "o7@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Shop 7", IsActive = true };
        var p1 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Pen", Price = 2m, Stock = 50, IsActive = true };
        var p2 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Pencil", Price = 1m, Stock = 50, IsActive = true };

        context.Users.AddRange(user, owner);
        context.Shops.Add(shop);
        context.Products.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);

        var cart = await cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = p1.Id, Quantity = 5 });
        cart = await cartService.AddItemAsync(user.Id, new AddCartItemRequest { ProductId = p2.Id, Quantity = 10 });
        Assert.Equal(2, cart.Items.Count);

        var penItem = cart.Items.First(i => i.ProductId == p1.Id);

        // Update quantity
        cart = await cartService.UpdateItemAsync(user.Id, penItem.Id, new UpdateCartItemRequest { Quantity = 8 });
        Assert.Equal(8, cart.Items.First(i => i.ProductId == p1.Id).Quantity);

        // Remove pen
        await cartService.RemoveItemAsync(user.Id, penItem.Id);
        cart = await cartService.GetCartAsync(user.Id);
        Assert.Single(cart.Items);
        Assert.Equal(p2.Id, cart.Items[0].ProductId);

        // Clear cart
        await cartService.ClearCartAsync(user.Id);
        cart = await cartService.GetCartAsync(user.Id);
        Assert.Empty(cart.Items);
        Assert.Equal(0, cart.CartTotal);
    }

    // ==========================================
    // 3. CHECKOUT & ATOMIC STOCK DECREMENT TESTS
    // ==========================================

    [Fact]
    public async Task Checkout_UsesAuthoritativeDbPrice_AndDecrementsStockAtomically()
    {
        using var context = TestDbContextFactory.Create(nameof(Checkout_UsesAuthoritativeDbPrice_AndDecrementsStockAtomically));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_chk", Email = "chk@b.com", DisplayName = "Buyer" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner_chk", Email = "chk@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Authorized Store", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Smartphone", Price = 999.99m, Stock = 5, IsActive = true };

        context.Users.AddRange(buyer, owner);
        context.Shops.Add(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        await cartService.AddItemAsync(buyer.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 2 });

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest
        {
            ShippingAddress = "100 Tech Blvd"
        });

        Assert.Single(orders);
        var order = orders.First();

        // Authoritative DB price 999.99 * 2 = 1999.98
        Assert.Equal(1999.98m, order.TotalPrice);
        Assert.Equal("100 Tech Blvd", order.ShippingAddress);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(999.99m, order.Items[0].UnitPrice);
        Assert.Equal("Smartphone", order.Items[0].ProductNameSnapshot);

        // Check stock decremented: 5 - 2 = 3
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(3, updatedProduct!.Stock);

        // Cart is cleared
        var remainingCart = await cartService.GetCartAsync(buyer.Id);
        Assert.Empty(remainingCart.Items);
    }

    [Fact]
    public async Task Checkout_MultiShopSeparation_CreatesSeparateOrdersPerShop()
    {
        using var context = TestDbContextFactory.Create(nameof(Checkout_MultiShopSeparation_CreatesSeparateOrdersPerShop));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_multi", Email = "bm@b.com", DisplayName = "Buyer" };
        var ownerA = new User { Id = Guid.NewGuid(), Username = "ownerA_m", Email = "oa_m@o.com", DisplayName = "Owner A", Role = UserRole.ShopOwner };
        var ownerB = new User { Id = Guid.NewGuid(), Username = "ownerB_m", Email = "ob_m@o.com", DisplayName = "Owner B", Role = UserRole.ShopOwner };

        var shopA = new Shop { Id = Guid.NewGuid(), OwnerId = ownerA.Id, Name = "Fashion Shop", IsActive = true };
        var shopB = new Shop { Id = Guid.NewGuid(), OwnerId = ownerB.Id, Name = "Shoes Shop", IsActive = true };

        var shirt = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, Name = "Shirt", Price = 30m, Stock = 10, IsActive = true };
        var pants = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, Name = "Pants", Price = 50m, Stock = 10, IsActive = true };
        var sneakers = new Product { Id = Guid.NewGuid(), ShopId = shopB.Id, Name = "Sneakers", Price = 80m, Stock = 10, IsActive = true };

        context.Users.AddRange(buyer, ownerA, ownerB);
        context.Shops.AddRange(shopA, shopB);
        context.Products.AddRange(shirt, pants, sneakers);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        await cartService.AddItemAsync(buyer.Id, new AddCartItemRequest { ProductId = shirt.Id, Quantity = 1 });
        await cartService.AddItemAsync(buyer.Id, new AddCartItemRequest { ProductId = pants.Id, Quantity = 2 });
        await cartService.AddItemAsync(buyer.Id, new AddCartItemRequest { ProductId = sneakers.Id, Quantity = 1 });

        var cart = await cartService.GetCartAsync(buyer.Id);
        Assert.Equal(2, cart.ShopGroups.Count);
        Assert.Equal(210m, cart.CartTotal); // 30 + 100 + 80 = 210

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "500 Market St" });

        Assert.Equal(2, orders.Count);

        var orderA = orders.First(o => o.ShopId == shopA.Id);
        Assert.Equal(130m, orderA.TotalPrice); // 30*1 + 50*2
        Assert.Equal(2, orderA.Items.Count);

        var orderB = orders.First(o => o.ShopId == shopB.Id);
        Assert.Equal(80m, orderB.TotalPrice); // 80*1
        Assert.Single(orderB.Items);
    }

    // ==========================================
    // 4. ORDER STATE MACHINE & CANCELLATION TESTS
    // ==========================================

    [Fact]
    public async Task OrderStateMachine_FullLifecycle_SetsDeliveredAt()
    {
        using var context = TestDbContextFactory.Create(nameof(OrderStateMachine_FullLifecycle_SetsDeliveredAt));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_life", Email = "bl@b.com", DisplayName = "Buyer" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner_life", Email = "ol@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shipper = new User { Id = Guid.NewGuid(), Username = "shipper_life", Email = "sl@s.com", DisplayName = "Shipper", Role = UserRole.Shipper };

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Lifecycle Shop", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Watch", Price = 100m, Stock = 10, IsActive = true };

        context.Users.AddRange(buyer, owner, shipper);
        context.Shops.Add(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        await cartService.AddItemAsync(buyer.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 1 });

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "Way" });
        var order = orders.First();

        // Initial state: Pending
        Assert.Equal("Pending", order.Status);

        // Shop confirms: Pending -> Confirmed
        await orderService.ConfirmAsync(order.Id, owner.Id);
        var confirmed = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("Confirmed", confirmed.Status);

        // Shop prepares: Confirmed -> Preparing
        await orderService.PrepareAsync(order.Id, owner.Id);
        var preparing = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("Preparing", preparing.Status);

        // Shop marks ready: Preparing -> WaitingForPickup
        await orderService.ReadyForPickupAsync(order.Id, owner.Id);
        var ready = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("WaitingForPickup", ready.Status);

        // Shipper claims: WaitingForPickup with ShipperId assigned
        await orderService.ClaimAsync(order.Id, shipper.Id);
        var claimed = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("WaitingForPickup", claimed.Status);
        Assert.Equal(shipper.Id, claimed.ShipperId);

        // Shipper picks up: WaitingForPickup -> Shipping
        await orderService.PickupAsync(order.Id, shipper.Id);
        var shipping = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("Shipping", shipping.Status);

        // Shipper delivers: Shipping -> Delivered, DeliveredAt recorded
        await orderService.DeliverAsync(order.Id, shipper.Id);
        var delivered = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("Delivered", delivered.Status);
        Assert.NotNull(delivered.DeliveredAt);
    }

    [Fact]
    public async Task Cancellation_RestoresStockOnce_AndDeliveredCannotBeCancelled()
    {
        using var context = TestDbContextFactory.Create(nameof(Cancellation_RestoresStockOnce_AndDeliveredCannotBeCancelled));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_can", Email = "can@b.com", DisplayName = "Buyer" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner_can", Email = "can@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Cancel Shop", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Book", Price = 20m, Stock = 10, IsActive = true };

        context.Users.AddRange(buyer, owner);
        context.Shops.Add(shop);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        await cartService.AddItemAsync(buyer.Id, new AddCartItemRequest { ProductId = product.Id, Quantity = 4 });

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "Home" });
        var order = orders.First();

        // Stock decreased: 10 - 4 = 6
        var pAfterOrder = await context.Products.FindAsync(product.Id);
        Assert.Equal(6, pAfterOrder!.Stock);

        // Cancel order
        await orderService.CancelAsync(order.Id, buyer.Id);
        var cancelledOrder = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("Cancelled", cancelledOrder.Status);
        Assert.NotNull(cancelledOrder.CancelledAt);

        // Stock restored: 6 + 4 = 10
        var pRestored = await context.Products.FindAsync(product.Id);
        Assert.Equal(10, pRestored!.Stock);

        // Repeated cancellation throws InvalidOperationException (idempotent, stock not double restored)
        await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.CancelAsync(order.Id, buyer.Id));
        var pStillRestored = await context.Products.FindAsync(product.Id);
        Assert.Equal(10, pStillRestored!.Stock);
    }

    [Fact]
    public async Task Cancellation_DeliveredOrder_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(Cancellation_DeliveredOrder_ThrowsInvalidOperationException));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_del", Email = "del@b.com", DisplayName = "Buyer" };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            ShopId = Guid.NewGuid(),
            Status = OrderStatus.Delivered,
            ShippingAddress = "Addr",
            DeliveredAt = DateTime.UtcNow
        };

        context.Users.Add(buyer);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);

        // Delivered cannot be cancelled
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.CancelAsync(order.Id, buyer.Id));
        Assert.Contains("Delivered orders cannot be cancelled", ex.Message);
    }

    [Fact]
    public async Task GetById_UnauthorizedUser_ThrowsUnauthorizedAccessException()
    {
        using var context = TestDbContextFactory.Create(nameof(GetById_UnauthorizedUser_ThrowsUnauthorizedAccessException));

        var buyer = new User { Id = Guid.NewGuid(), Username = "b_auth", Email = "ba@b.com", DisplayName = "Buyer" };
        var intruder = new User { Id = Guid.NewGuid(), Username = "intruder", Email = "i@b.com", DisplayName = "Intruder" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "shop_o", Email = "so@b.com", DisplayName = "Shop Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Shop Auth" };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            ShopId = shop.Id,
            Status = OrderStatus.Pending,
            ShippingAddress = "Addr"
        };

        context.Users.AddRange(buyer, intruder, shopOwner);
        context.Shops.Add(shop);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);

        // Buyer can view
        var resBuyer = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.NotNull(resBuyer);

        // ShopOwner can view
        var resOwner = await orderService.GetByIdAsync(order.Id, shopOwner.Id);
        Assert.NotNull(resOwner);

        // Intruder cannot view
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.GetByIdAsync(order.Id, intruder.Id));
    }

    // ==========================================
    // 5. REVENUE CALCULATION TESTS
    // ==========================================

    [Fact]
    public async Task Revenue_OnlyDeliveredOrdersCounted_DeliveredAtDateRange_ShopIsolation()
    {
        using var context = TestDbContextFactory.Create(nameof(Revenue_OnlyDeliveredOrdersCounted_DeliveredAtDateRange_ShopIsolation));

        var ownerA = new User { Id = Guid.NewGuid(), Username = "revA", Email = "ra@r.com", DisplayName = "Owner A", Role = UserRole.ShopOwner };
        var ownerB = new User { Id = Guid.NewGuid(), Username = "revB", Email = "rb@r.com", DisplayName = "Owner B", Role = UserRole.ShopOwner };
        var shipper = new User { Id = Guid.NewGuid(), Username = "revS", Email = "rs@r.com", DisplayName = "Shipper", Role = UserRole.Shipper };

        var shopA = new Shop { Id = Guid.NewGuid(), OwnerId = ownerA.Id, Name = "Shop Rev A" };
        var shopB = new Shop { Id = Guid.NewGuid(), OwnerId = ownerB.Id, Name = "Shop Rev B" };

        var now = DateTime.UtcNow;

        // Order 1 for Shop A: Delivered today, Total = $100, Items = 2, ShippingFee = $10
        var orderA1 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shopA.Id,
            ShipperId = shipper.Id,
            Status = OrderStatus.Delivered,
            TotalPrice = 100m,
            ShippingFee = 10m,
            ShippingAddress = "A",
            CreatedAt = now.AddDays(-2),
            DeliveredAt = now
        };
        orderA1.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = orderA1.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item 1", UnitPrice = 50m, Quantity = 2 });

        // Order 2 for Shop A: Pending (not delivered), Total = $200
        var orderA2 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shopA.Id,
            Status = OrderStatus.Pending,
            TotalPrice = 200m,
            ShippingAddress = "A",
            CreatedAt = now
        };

        // Order 3 for Shop A: Delivered 10 days ago (outside target range), Total = $300
        var orderA3 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shopA.Id,
            Status = OrderStatus.Delivered,
            TotalPrice = 300m,
            ShippingAddress = "A",
            CreatedAt = now.AddDays(-12),
            DeliveredAt = now.AddDays(-10)
        };

        // Order for Shop B: Delivered today, Total = $500, Items = 5, ShippingFee = $15
        var orderB = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shopB.Id,
            ShipperId = shipper.Id,
            Status = OrderStatus.Delivered,
            TotalPrice = 500m,
            ShippingFee = 15m,
            ShippingAddress = "B",
            CreatedAt = now.AddDays(-1),
            DeliveredAt = now
        };
        orderB.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = orderB.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item B", UnitPrice = 100m, Quantity = 5 });

        context.Users.AddRange(ownerA, ownerB, shipper);
        context.Shops.AddRange(shopA, shopB);
        context.Orders.AddRange(orderA1, orderA2, orderA3, orderB);
        await context.SaveChangesAsync();

        var revenueService = new RevenueService(context);

        // Shop A revenue for the last 5 days to tomorrow
        var revA = await revenueService.GetShopRevenueAsync(ownerA.Id, now.AddDays(-5), now.AddDays(1));
        Assert.Equal(100m, revA.TotalRevenue);
        Assert.Equal(100m, revA.GrossMerchandiseValue);
        Assert.Equal(1, revA.TotalOrders);
        Assert.Equal(1, revA.DeliveredOrderCount);
        Assert.Equal(2, revA.TotalItemsSold);
        Assert.Equal(10m, revA.TotalShippingFees);

        // Shop B revenue
        var revB = await revenueService.GetShopRevenueAsync(ownerB.Id, now.AddDays(-5), now.AddDays(1));
        Assert.Equal(500m, revB.TotalRevenue);
        Assert.Equal(1, revB.TotalOrders);
        Assert.Equal(5, revB.TotalItemsSold);

        // Shipper revenue (earned shipping fee for both delivered orders in date range)
        var revShipper = await revenueService.GetShipperRevenueAsync(shipper.Id, now.AddDays(-5), now.AddDays(1));
        Assert.Equal(25m, revShipper.TotalRevenue); // 10 + 15 = 25
        Assert.Equal(2, revShipper.TotalOrders);
    }

    // ==========================================
    // 6. THREE AUTOMATED SIMULATION FLOWS
    // ==========================================

    [Fact]
    public async Task SimulationFlow_1_CustomerFlow()
    {
        // 1. Search products -> 2. View shop/catalog -> 3. Add to cart -> 4. Checkout -> 5. View orders
        using var context = TestDbContextFactory.Create(nameof(SimulationFlow_1_CustomerFlow));

        var customer = new User { Id = Guid.NewGuid(), Username = "sim_customer", Email = "sc@c.com", DisplayName = "Sim Customer" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "sim_owner", Email = "so@o.com", DisplayName = "Sim Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Gadget World", IsActive = true };
        var category = new Category { Id = Guid.NewGuid(), Name = "Smartphones" };

        var phone = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "ZolaPhone Ultra", Price = 799m, Stock = 10, IsActive = true };
        var caseCover = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "ZolaPhone Case", Price = 25m, Stock = 50, IsActive = true };

        context.Users.AddRange(customer, shopOwner);
        context.Shops.Add(shop);
        context.Categories.Add(category);
        context.Products.AddRange(phone, caseCover);
        await context.SaveChangesAsync();

        var searchService = new SearchService(context);
        var cartService = new CartService(context);
        var orderService = new OrderService(context);

        // Step 1: Search products
        var searchResult = await searchService.SearchProductsAsync(new ProductSearchParams { Q = "ZolaPhone" });
        Assert.Equal(2, searchResult.TotalItems);

        // Step 2: Add to cart
        await cartService.AddItemAsync(customer.Id, new AddCartItemRequest { ProductId = phone.Id, Quantity = 1 });
        await cartService.AddItemAsync(customer.Id, new AddCartItemRequest { ProductId = caseCover.Id, Quantity = 2 });

        var cart = await cartService.GetCartAsync(customer.Id);
        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(849m, cart.CartTotal); // 799*1 + 25*2 = 849

        // Step 3: Checkout
        var placedOrders = await orderService.CreateAsync(customer.Id, new CreateOrderRequest
        {
            ShippingAddress = "123 Saigon St, District 1, HCMC"
        });

        Assert.Single(placedOrders);
        var order = placedOrders.First();
        Assert.Equal(849m, order.TotalPrice);
        Assert.Equal("Pending", order.Status);

        // Step 4: Verify stock decremented
        var updatedPhone = await context.Products.FindAsync(phone.Id);
        var updatedCase = await context.Products.FindAsync(caseCover.Id);
        Assert.Equal(9, updatedPhone!.Stock);
        Assert.Equal(48, updatedCase!.Stock);

        // Step 5: View customer's order history
        var myOrders = await orderService.GetBuyerOrdersAsync(customer.Id, new PaginationParams { Page = 1, PageSize = 10 });
        Assert.Single(myOrders.Items);
        Assert.Equal(order.Id, myOrders.Items[0].Id);
    }

    [Fact]
    public async Task SimulationFlow_2_DeliveryFlow()
    {
        // 1. Order created -> 2. Shop confirms -> 3. Shop prepares -> 4. Shop ready -> 5. Shipper claims -> 6. Shipper picks up -> 7. Shipper delivers -> 8. Revenue updated
        using var context = TestDbContextFactory.Create(nameof(SimulationFlow_2_DeliveryFlow));

        var customer = new User { Id = Guid.NewGuid(), Username = "flow2_buyer", Email = "f2b@b.com", DisplayName = "Buyer" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "flow2_owner", Email = "f2o@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shipper = new User { Id = Guid.NewGuid(), Username = "flow2_shipper", Email = "f2s@s.com", DisplayName = "Shipper", Role = UserRole.Shipper };

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Coffee Central", IsActive = true };
        var coffeeBean = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Arabica Beans 500g", Price = 15m, Stock = 20, IsActive = true };

        context.Users.AddRange(customer, shopOwner, shipper);
        context.Shops.Add(shop);
        context.Products.Add(coffeeBean);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        var orderService = new OrderService(context);
        var revenueService = new RevenueService(context);

        // Step 1: Create Order
        await cartService.AddItemAsync(customer.Id, new AddCartItemRequest { ProductId = coffeeBean.Id, Quantity = 3 });
        var orders = await orderService.CreateAsync(customer.Id, new CreateOrderRequest { ShippingAddress = "Coffee Lover Ave" });
        var orderId = orders.First().Id;

        // Step 2: Shop confirms
        await orderService.ConfirmAsync(orderId, shopOwner.Id);

        // Step 3: Shop prepares
        await orderService.PrepareAsync(orderId, shopOwner.Id);

        // Step 4: Shop ready for pickup
        await orderService.ReadyForPickupAsync(orderId, shopOwner.Id);

        // Step 5: Shipper views available orders and claims
        var available = await orderService.GetAvailableForShipperAsync(new PaginationParams());
        Assert.Contains(available.Items, o => o.Id == orderId);
        await orderService.ClaimAsync(orderId, shipper.Id);

        // Step 6: Shipper picks up
        await orderService.PickupAsync(orderId, shipper.Id);

        // Step 7: Shipper delivers
        await orderService.DeliverAsync(orderId, shipper.Id);

        // Verify Delivered status & timestamp
        var delivered = await orderService.GetByIdAsync(orderId, customer.Id);
        Assert.Equal("Delivered", delivered.Status);
        Assert.NotNull(delivered.DeliveredAt);

        // Step 8: Verify Shop Revenue reflects delivered order
        var shopRevenue = await revenueService.GetShopRevenueAsync(shopOwner.Id, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.Equal(45m, shopRevenue.TotalRevenue); // 15 * 3
        Assert.Equal(1, shopRevenue.TotalOrders);
        Assert.Equal(3, shopRevenue.TotalItemsSold);
    }

    [Fact]
    public async Task SimulationFlow_3_CancellationFlow()
    {
        // 1. Order created -> 2. Stock decremented -> 3. Order cancelled -> 4. Stock restored -> 5. Repeat cancel rejected
        using var context = TestDbContextFactory.Create(nameof(SimulationFlow_3_CancellationFlow));

        var customer = new User { Id = Guid.NewGuid(), Username = "flow3_buyer", Email = "f3b@b.com", DisplayName = "Buyer" };
        var shopOwner = new User { Id = Guid.NewGuid(), Username = "flow3_owner", Email = "f3o@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = shopOwner.Id, Name = "Book Nook", IsActive = true };
        var book = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Clean Architecture Book", Price = 40m, Stock = 8, IsActive = true };

        context.Users.AddRange(customer, shopOwner);
        context.Shops.Add(shop);
        context.Products.Add(book);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        var orderService = new OrderService(context);

        // Step 1: Create Order
        await cartService.AddItemAsync(customer.Id, new AddCartItemRequest { ProductId = book.Id, Quantity = 3 });
        var orders = await orderService.CreateAsync(customer.Id, new CreateOrderRequest { ShippingAddress = "Reader Way" });
        var orderId = orders.First().Id;

        // Step 2: Stock decremented: 8 - 3 = 5
        var pAfterOrder = await context.Products.FindAsync(book.Id);
        Assert.Equal(5, pAfterOrder!.Stock);

        // Step 3: Cancel Order
        await orderService.CancelAsync(orderId, customer.Id);

        var cancelled = await orderService.GetByIdAsync(orderId, customer.Id);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);

        // Step 4: Stock restored: 5 + 3 = 8
        var pRestored = await context.Products.FindAsync(book.Id);
        Assert.Equal(8, pRestored!.Stock);

        // Step 5: Duplicate cancellation attempt rejected
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.CancelAsync(orderId, customer.Id));
        Assert.Contains("Order is already cancelled", ex.Message);

        // Verify stock remains 8 (not double incremented)
        var pFinal = await context.Products.FindAsync(book.Id);
        Assert.Equal(8, pFinal!.Stock);
    }

    // ==========================================
    // 7. CONCURRENCY & PRODUCT CATALOG TESTS
    // ==========================================

    [Fact]
    public async Task Concurrency_ConcurrentCheckout_StockNeverNegative()
    {
        using var context = TestDbContextFactory.Create(nameof(Concurrency_ConcurrentCheckout_StockNeverNegative));

        var buyer1 = new User { Id = Guid.NewGuid(), Username = "c_buyer1", Email = "cb1@b.com", DisplayName = "Buyer 1" };
        var buyer2 = new User { Id = Guid.NewGuid(), Username = "c_buyer2", Email = "cb2@b.com", DisplayName = "Buyer 2" };
        var owner = new User { Id = Guid.NewGuid(), Username = "c_owner", Email = "co@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Flash Store", IsActive = true };
        var limitedProduct = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Last Console", Price = 499m, Stock = 1, IsActive = true };

        context.Users.AddRange(buyer1, buyer2, owner);
        context.Shops.Add(shop);
        context.Products.Add(limitedProduct);
        await context.SaveChangesAsync();

        var cartService = new CartService(context);
        await cartService.AddItemAsync(buyer1.Id, new AddCartItemRequest { ProductId = limitedProduct.Id, Quantity = 1 });
        await cartService.AddItemAsync(buyer2.Id, new AddCartItemRequest { ProductId = limitedProduct.Id, Quantity = 1 });

        var orderService = new OrderService(context);

        // Buyer 1 checks out first and claims the last unit
        var order1 = await orderService.CreateAsync(buyer1.Id, new CreateOrderRequest { ShippingAddress = "Addr 1" });
        Assert.Single(order1);

        // Buyer 2 attempts to checkout the same item whose stock is now 0
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.CreateAsync(buyer2.Id, new CreateOrderRequest { ShippingAddress = "Addr 2" }));
        Assert.Contains("Insufficient stock", ex.Message);

        // Verify stock is exactly 0 and never went negative
        var p = await context.Products.FindAsync(limitedProduct.Id);
        Assert.Equal(0, p!.Stock);
    }

    [Fact]
    public async Task Concurrency_ShipperClaim_OnlyOneSucceeds()
    {
        using var context = TestDbContextFactory.Create(nameof(Concurrency_ShipperClaim_OnlyOneSucceeds));

        var buyer = new User { Id = Guid.NewGuid(), Username = "ship_b", Email = "sb@b.com", DisplayName = "Buyer" };
        var shipper1 = new User { Id = Guid.NewGuid(), Username = "s1_claim", Email = "s1@c.com", DisplayName = "Shipper 1", Role = UserRole.Shipper };
        var shipper2 = new User { Id = Guid.NewGuid(), Username = "s2_claim", Email = "s2@c.com", DisplayName = "Shipper 2", Role = UserRole.Shipper };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = buyer.Id, Name = "Pickup Shop", IsActive = true };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            ShopId = shop.Id,
            Status = OrderStatus.WaitingForPickup,
            ShippingAddress = "123 Port St",
            ShipperId = null
        };

        context.Users.AddRange(buyer, shipper1, shipper2);
        context.Shops.Add(shop);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);

        // Shipper 1 claims order
        await orderService.ClaimAsync(order.Id, shipper1.Id);

        // Shipper 2 tries to claim already claimed order
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.ClaimAsync(order.Id, shipper2.Id));
        Assert.Contains("already claimed", ex.Message);

        var finalOrder = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal(shipper1.Id, finalOrder.ShipperId);
    }

    [Fact]
    public async Task ProductService_GetByShop_SupportsCategoryAndSort()
    {
        using var context = TestDbContextFactory.Create(nameof(ProductService_GetByShop_SupportsCategoryAndSort));

        var owner = new User { Id = Guid.NewGuid(), Username = "ps_owner", Email = "pso@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Catalog Shop", IsActive = true };
        var cat1 = new Category { Id = Guid.NewGuid(), Name = "Laptops" };
        var cat2 = new Category { Id = Guid.NewGuid(), Name = "Accessories" };

        var p1 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = cat1.Id, Name = "Laptop High", Price = 1500m, Stock = 5, IsActive = true };
        var p2 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = cat1.Id, Name = "Laptop Mid", Price = 800m, Stock = 5, IsActive = true };
        var p3 = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = cat2.Id, Name = "Mouse Cable", Price = 10m, Stock = 50, IsActive = true };

        context.Users.Add(owner);
        context.Shops.Add(shop);
        context.Categories.AddRange(cat1, cat2);
        context.Products.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        var productService = new ProductService(context);

        // Filter by category
        var catResult = await productService.GetByShopAsync(shop.Id, new PaginationParams { Page = 1, PageSize = 10 }, cat1.Id);
        Assert.Equal(2, catResult.TotalItems);
        Assert.All(catResult.Items, p => Assert.Equal(cat1.Id, p.CategoryId));

        // Sort by price_desc
        var sortResult = await productService.GetByShopAsync(shop.Id, new PaginationParams { Page = 1, PageSize = 10 }, null, "price_desc");
        Assert.Equal(3, sortResult.TotalItems);
        Assert.Equal("Laptop High", sortResult.Items[0].Name);
        Assert.Equal("Laptop Mid", sortResult.Items[1].Name);
        Assert.Equal("Mouse Cable", sortResult.Items[2].Name);
    }

    [Fact]
    public async Task Revenue_StrictDeliveredAt_CreatedAtNeverSubstitutes()
    {
        using var context = TestDbContextFactory.Create(nameof(Revenue_StrictDeliveredAt_CreatedAtNeverSubstitutes));

        var owner = new User { Id = Guid.NewGuid(), Username = "strict_owner", Email = "so@so.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Strict Shop", IsActive = true };

        var now = DateTime.UtcNow;
        var from = now.AddDays(-2);
        var to = now.AddDays(2);

        // Order 1: Delivered, DeliveredAt in range -> COUNTS ($100, 2 items)
        var order1 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shop.Id,
            Status = OrderStatus.Delivered,
            TotalPrice = 100m,
            ShippingFee = 10m,
            ShippingAddress = "Addr 1",
            CreatedAt = now.AddDays(-10),
            DeliveredAt = now
        };
        order1.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order1.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item 1", UnitPrice = 50m, Quantity = 2 });

        // Order 2: Delivered, but DeliveredAt == null, CreatedAt in range -> MUST NOT COUNT ($200)
        var order2 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shop.Id,
            Status = OrderStatus.Delivered,
            TotalPrice = 200m,
            ShippingFee = 15m,
            ShippingAddress = "Addr 2",
            CreatedAt = now,
            DeliveredAt = null
        };
        order2.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order2.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item 2", UnitPrice = 200m, Quantity = 1 });

        // Order 3: Delivered, CreatedAt in range, but DeliveredAt OUTSIDE range -> MUST NOT COUNT ($300)
        var order3 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shop.Id,
            Status = OrderStatus.Delivered,
            TotalPrice = 300m,
            ShippingFee = 20m,
            ShippingAddress = "Addr 3",
            CreatedAt = now,
            DeliveredAt = now.AddDays(-10)
        };
        order3.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order3.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item 3", UnitPrice = 300m, Quantity = 1 });

        // Order 4: Non-Delivered (Pending), CreatedAt in range -> MUST NOT COUNT ($400)
        var order4 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shop.Id,
            Status = OrderStatus.Pending,
            TotalPrice = 400m,
            ShippingFee = 25m,
            ShippingAddress = "Addr 4",
            CreatedAt = now,
            DeliveredAt = null
        };
        order4.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order4.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item 4", UnitPrice = 400m, Quantity = 1 });

        // Order 5: Non-Delivered (Shipping), CreatedAt in range -> MUST NOT COUNT ($500)
        var order5 = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            ShopId = shop.Id,
            Status = OrderStatus.Shipping,
            TotalPrice = 500m,
            ShippingFee = 30m,
            ShippingAddress = "Addr 5",
            CreatedAt = now,
            DeliveredAt = null
        };
        order5.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order5.Id, ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item 5", UnitPrice = 500m, Quantity = 1 });

        context.Users.Add(owner);
        context.Shops.Add(shop);
        context.Orders.AddRange(order1, order2, order3, order4, order5);
        await context.SaveChangesAsync();

        var revenueService = new RevenueService(context);
        var revenue = await revenueService.GetShopRevenueAsync(owner.Id, from, to);

        // Strictly Order 1 only!
        Assert.Equal(100m, revenue.TotalRevenue);
        Assert.Equal(1, revenue.TotalOrders);
        Assert.Equal(2, revenue.TotalItemsSold);
        Assert.Equal(10m, revenue.TotalShippingFees);
    }

    [Fact]
    public async Task ShipperClaim_NotifiesBuyerAndShopOwner_AndFailedClaimDoesNotNotify()
    {
        using var context = TestDbContextFactory.Create(nameof(ShipperClaim_NotifiesBuyerAndShopOwner_AndFailedClaimDoesNotNotify));

        var buyer = new User { Id = Guid.NewGuid(), Username = "claim_b", Email = "cb@cb.com", DisplayName = "Buyer" };
        var owner = new User { Id = Guid.NewGuid(), Username = "claim_o", Email = "co@co.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shipper1 = new User { Id = Guid.NewGuid(), Username = "claim_s1", Email = "cs1@cs.com", DisplayName = "Shipper 1", Role = UserRole.Shipper };
        var shipper2 = new User { Id = Guid.NewGuid(), Username = "claim_s2", Email = "cs2@cs.com", DisplayName = "Shipper 2", Role = UserRole.Shipper };

        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Notification Claim Shop", IsActive = true };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            ShopId = shop.Id,
            Status = OrderStatus.WaitingForPickup,
            ShippingAddress = "789 Speed Way",
            ShipperId = null
        };

        context.Users.AddRange(buyer, owner, shipper1, shipper2);
        context.Shops.Add(shop);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var testNotificationService = new TestNotificationService();
        var orderService = new OrderService(context, testNotificationService);

        // Shipper 1 successfully claims order
        await orderService.ClaimAsync(order.Id, shipper1.Id);

        // Must notify exactly 2 recipients: Buyer and ShopOwner
        Assert.Equal(2, testNotificationService.RecordedNotifications.Count);

        Assert.Contains(testNotificationService.RecordedNotifications,
            n => n.UserId == buyer.Id && n.Type == NotificationType.OrderClaimed && n.RelatedEntityId == order.Id);

        Assert.Contains(testNotificationService.RecordedNotifications,
            n => n.UserId == owner.Id && n.Type == NotificationType.OrderClaimed && n.RelatedEntityId == order.Id);

        // Shipper 2 attempts to claim the already claimed order -> Throws exception
        await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.ClaimAsync(order.Id, shipper2.Id));

        // Total recorded notifications must remain strictly 2 (no duplicate or phantom notifications)
        Assert.Equal(2, testNotificationService.RecordedNotifications.Count);
    }
}

public class TestNotificationService : INotificationService
{
    public List<(Guid UserId, NotificationType Type, string Title, string Content, Guid? RelatedEntityId)> RecordedNotifications { get; } = new();

    public Task<NotificationDto> CreateAsync(Guid userId, NotificationType type, string title, string content, Guid? relatedEntityId = null)
    {
        RecordedNotifications.Add((userId, type, title, content, relatedEntityId));
        return Task.FromResult(new NotificationDto
        {
            Id = Guid.NewGuid(),
            Type = type.ToString(),
            Title = title,
            Content = content,
            RelatedEntityId = relatedEntityId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public Task<NotificationDto> CreateOrUpdateLikeNotificationAsync(Guid targetUserId, Guid actorUserId, Guid postId) => throw new NotImplementedException();
    public Task<PagedResult<NotificationDto>> GetByUserAsync(Guid userId, PaginationParams pagination) => throw new NotImplementedException();
    public Task<int> GetUnreadCountAsync(Guid userId) => throw new NotImplementedException();
    public Task<NotificationDto> MarkAsReadAsync(Guid notificationId, Guid userId) => throw new NotImplementedException();
    public Task MarkAllAsReadAsync(Guid userId) => throw new NotImplementedException();
}
