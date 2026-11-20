using Microsoft.EntityFrameworkCore;
using MySocialApp.Infrastructure.Data;

namespace MySocialApp.Tests;

public static class TestDbContextFactory
{
    public static MySocialAppDbContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<MySocialAppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new MySocialAppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
