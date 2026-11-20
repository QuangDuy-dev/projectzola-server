using MySocialApp.Application.DTOs.Orders;
using MySocialApp.Application.Services;
using MySocialApp.Domain.Entities;
using MySocialApp.Domain.Enums;

namespace MySocialApp.Tests;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrder_FromCart_DeductsStockAndCreatesOrder()
    {
        using var context = TestDbContextFactory.Create(nameof(CreateOrder_FromCart_DeductsStockAndCreatesOrder));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer", Email = "b@b.com", DisplayName = "Buyer" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner", Email = "o@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Test Shop" };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Keyboard", Price = 50.0m, Stock = 10 };
        var cart = new Cart { Id = Guid.NewGuid(), UserId = buyer.Id };
        var cartItem = new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = product.Id, Quantity = 2 };
        cart.Items.Add(cartItem);

        context.Users.AddRange(buyer, owner);
        context.Shops.Add(shop);
        context.Products.Add(product);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "123 Main St" });

        Assert.NotNull(orders);
        Assert.Single(orders);
        var order = orders.First();
        Assert.Equal("Pending", order.Status);
        Assert.Equal(100.0m, order.TotalPrice);
        Assert.Single(order.Items);
        Assert.Equal("Keyboard", order.Items[0].ProductNameSnapshot);

        // Verify stock deducted
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(8, updatedProduct!.Stock);

        // Verify cart is cleared
        var remainingCartItems = context.CartItems.Where(ci => ci.CartId == cart.Id).ToList();
        Assert.Empty(remainingCartItems);
    }

    [Fact]
    public async Task CreateOrder_MultiShopCart_CreatesSeparateOrders()
    {
        using var context = TestDbContextFactory.Create(nameof(CreateOrder_MultiShopCart_CreatesSeparateOrders));

        var buyer = new User { Id = Guid.NewGuid(), Username = "multibuyer", Email = "mb@b.com", DisplayName = "Multi Buyer" };
        var ownerA = new User { Id = Guid.NewGuid(), Username = "ownerA", Email = "oa@oa.com", DisplayName = "Owner A", Role = UserRole.ShopOwner };
        var ownerB = new User { Id = Guid.NewGuid(), Username = "ownerB", Email = "ob@ob.com", DisplayName = "Owner B", Role = UserRole.ShopOwner };

        var shopA = new Shop { Id = Guid.NewGuid(), OwnerId = ownerA.Id, Name = "Shop A" };
        var shopB = new Shop { Id = Guid.NewGuid(), OwnerId = ownerB.Id, Name = "Shop B" };

        var productA1 = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, Name = "Product A1", Price = 10.0m, Stock = 10 };
        var productA2 = new Product { Id = Guid.NewGuid(), ShopId = shopA.Id, Name = "Product A2", Price = 20.0m, Stock = 20 };
        var productB1 = new Product { Id = Guid.NewGuid(), ShopId = shopB.Id, Name = "Product B1", Price = 30.0m, Stock = 15 };

        var cart = new Cart { Id = Guid.NewGuid(), UserId = buyer.Id };
        cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = productA1.Id, Quantity = 2 });
        cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = productA2.Id, Quantity = 3 });
        cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = productB1.Id, Quantity = 4 });

        context.Users.AddRange(buyer, ownerA, ownerB);
        context.Shops.AddRange(shopA, shopB);
        context.Products.AddRange(productA1, productA2, productB1);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "789 High St" });

        // Verify 2 separate orders created
        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);

        // Verify Order A
        var orderA = orders.FirstOrDefault(o => o.ShopId == shopA.Id);
        Assert.NotNull(orderA);
        Assert.Equal(2, orderA.Items.Count);
        Assert.Equal(80.0m, orderA.TotalPrice); // 2*10 + 3*20 = 80
        var itemA1 = orderA.Items.First(i => i.ProductId == productA1.Id);
        Assert.Equal("Product A1", itemA1.ProductNameSnapshot);
        Assert.Equal(10.0m, itemA1.UnitPrice);
        Assert.Equal(2, itemA1.Quantity);
        var itemA2 = orderA.Items.First(i => i.ProductId == productA2.Id);
        Assert.Equal("Product A2", itemA2.ProductNameSnapshot);
        Assert.Equal(20.0m, itemA2.UnitPrice);
        Assert.Equal(3, itemA2.Quantity);

        // Verify Order B
        var orderB = orders.FirstOrDefault(o => o.ShopId == shopB.Id);
        Assert.NotNull(orderB);
        Assert.Single(orderB.Items);
        Assert.Equal(120.0m, orderB.TotalPrice); // 4*30 = 120
        var itemB1 = orderB.Items.First(i => i.ProductId == productB1.Id);
        Assert.Equal("Product B1", itemB1.ProductNameSnapshot);
        Assert.Equal(30.0m, itemB1.UnitPrice);
        Assert.Equal(4, itemB1.Quantity);

        // Verify stock deductions
        var updatedA1 = await context.Products.FindAsync(productA1.Id);
        var updatedA2 = await context.Products.FindAsync(productA2.Id);
        var updatedB1 = await context.Products.FindAsync(productB1.Id);
        Assert.Equal(8, updatedA1!.Stock);   // 10 - 2 = 8
        Assert.Equal(17, updatedA2!.Stock);  // 20 - 3 = 17
        Assert.Equal(11, updatedB1!.Stock);  // 15 - 4 = 11

        // Verify cart is cleared
        var remainingCartItems = context.CartItems.Where(ci => ci.CartId == cart.Id).ToList();
        Assert.Empty(remainingCartItems);
    }

    [Fact]
    public async Task CancelOrder_RestoresStock()
    {
        using var context = TestDbContextFactory.Create(nameof(CancelOrder_RestoresStock));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer2", Email = "b2@b.com", DisplayName = "Buyer" };
        var owner = new User { Id = Guid.NewGuid(), Username = "owner2", Email = "o2@o.com", DisplayName = "Owner", Role = UserRole.ShopOwner };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = owner.Id, Name = "Test Shop 2" };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Mouse", Price = 20.0m, Stock = 10 };
        var cart = new Cart { Id = Guid.NewGuid(), UserId = buyer.Id };
        cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = product.Id, Quantity = 3 });

        context.Users.AddRange(buyer, owner);
        context.Shops.Add(shop);
        context.Products.Add(product);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);
        var orders = await orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "456 Oak St" });
        var order = orders.First();

        // Cancel order
        await orderService.CancelAsync(order.Id, buyer.Id);

        var cancelledOrder = await orderService.GetByIdAsync(order.Id, buyer.Id);
        Assert.Equal("Cancelled", cancelledOrder.Status);

        var restoredProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(10, restoredProduct!.Stock);
    }

    [Fact]
    public async Task InvalidTransition_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(InvalidTransition_ThrowsInvalidOperationException));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer3", Email = "b3@b.com", DisplayName = "Buyer" };
        var shipper = new User { Id = Guid.NewGuid(), Username = "shipper3", Email = "s3@s.com", DisplayName = "Shipper", Role = UserRole.Shipper };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyer.Id,
            ShopId = Guid.NewGuid(),
            ShippingAddress = "789 Pine St",
            Status = OrderStatus.Pending,
            ShipperId = shipper.Id,
            TotalPrice = 100m
        };

        context.Users.AddRange(buyer, shipper);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);

        // Pending -> Deliver directly is illegal
        await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.DeliverAsync(order.Id, shipper.Id));
    }

    [Fact]
    public async Task Shipper_ClaimOrder_EnforcesRulesAndTransitions()
    {
        using var context = TestDbContextFactory.Create(nameof(Shipper_ClaimOrder_EnforcesRulesAndTransitions));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer4", Email = "b4@b.com", DisplayName = "Buyer" };
        var shipper1 = new User { Id = Guid.NewGuid(), Username = "shipper1", Email = "s1@s.com", DisplayName = "Shipper 1", Role = UserRole.Shipper };
        var shipper2 = new User { Id = Guid.NewGuid(), Username = "shipper2", Email = "s2@s.com", DisplayName = "Shipper 2", Role = UserRole.Shipper };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = buyer.Id, Name = "Shop 4" };

        var pendingOrder = new Order { Id = Guid.NewGuid(), BuyerId = buyer.Id, ShopId = shop.Id, Status = OrderStatus.Pending, ShippingAddress = "Addr" };
        var waitingOrder = new Order { Id = Guid.NewGuid(), BuyerId = buyer.Id, ShopId = shop.Id, Status = OrderStatus.WaitingForPickup, ShippingAddress = "Addr" };

        context.Users.AddRange(buyer, shipper1, shipper2);
        context.Shops.Add(shop);
        context.Orders.AddRange(pendingOrder, waitingOrder);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);

        // Cannot claim Pending order
        await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.ClaimAsync(pendingOrder.Id, shipper1.Id));

        // Can claim WaitingForPickup order
        await orderService.ClaimAsync(waitingOrder.Id, shipper1.Id);
        var claimed = await orderService.GetByIdAsync(waitingOrder.Id, buyer.Id);
        Assert.Equal(shipper1.Id, claimed.ShipperId);

        // Another shipper cannot claim the same order
        await Assert.ThrowsAsync<InvalidOperationException>(() => orderService.ClaimAsync(waitingOrder.Id, shipper2.Id));

        // Another shipper cannot pickup or deliver shipper1's order
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.PickupAsync(waitingOrder.Id, shipper2.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.DeliverAsync(waitingOrder.Id, shipper2.Id));

        // Shipper1 pickups: WaitingForPickup -> Shipping
        await orderService.PickupAsync(waitingOrder.Id, shipper1.Id);
        var inShipping = await orderService.GetByIdAsync(waitingOrder.Id, buyer.Id);
        Assert.Equal("Shipping", inShipping.Status);

        // Shipper1 delivers: Shipping -> Delivered
        await orderService.DeliverAsync(waitingOrder.Id, shipper1.Id);
        var delivered = await orderService.GetByIdAsync(waitingOrder.Id, buyer.Id);
        Assert.Equal("Delivered", delivered.Status);
    }

    [Fact]
    public async Task Buyer_CannotCancelAnotherBuyersOrder()
    {
        using var context = TestDbContextFactory.Create(nameof(Buyer_CannotCancelAnotherBuyersOrder));

        var buyer1 = new User { Id = Guid.NewGuid(), Username = "buyer1", Email = "b1@b.com", DisplayName = "Buyer 1" };
        var buyer2 = new User { Id = Guid.NewGuid(), Username = "buyer2", Email = "b2@b.com", DisplayName = "Buyer 2" };
        var order = new Order { Id = Guid.NewGuid(), BuyerId = buyer1.Id, ShopId = Guid.NewGuid(), Status = OrderStatus.Pending, ShippingAddress = "Addr" };

        context.Users.AddRange(buyer1, buyer2);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => orderService.CancelAsync(order.Id, buyer2.Id));
    }

    [Fact]
    public async Task CreateOrder_InactiveProduct_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(CreateOrder_InactiveProduct_ThrowsInvalidOperationException));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_inactive", Email = "bi@b.com", DisplayName = "Buyer" };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = buyer.Id, Name = "Shop Inactive" };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Inactive Item", Price = 10m, Stock = 5, IsActive = false };
        var cart = new Cart { Id = Guid.NewGuid(), UserId = buyer.Id };
        cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = product.Id, Quantity = 1 });

        context.Users.Add(buyer);
        context.Shops.Add(shop);
        context.Products.Add(product);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "Addr" }));

        Assert.Contains("no longer active", ex.Message);
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_ThrowsInvalidOperationException()
    {
        using var context = TestDbContextFactory.Create(nameof(CreateOrder_InsufficientStock_ThrowsInvalidOperationException));

        var buyer = new User { Id = Guid.NewGuid(), Username = "buyer_out", Email = "bo@b.com", DisplayName = "Buyer" };
        var shop = new Shop { Id = Guid.NewGuid(), OwnerId = buyer.Id, Name = "Shop Out" };
        var product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, Name = "Limited Item", Price = 10m, Stock = 2, IsActive = true };
        var cart = new Cart { Id = Guid.NewGuid(), UserId = buyer.Id };
        cart.Items.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = product.Id, Quantity = 5 }); // Wants 5, only 2 in stock

        context.Users.Add(buyer);
        context.Shops.Add(shop);
        context.Products.Add(product);
        context.Carts.Add(cart);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orderService.CreateAsync(buyer.Id, new CreateOrderRequest { ShippingAddress = "Addr" }));

        Assert.Contains("Insufficient stock", ex.Message);
    }
}
