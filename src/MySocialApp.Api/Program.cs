using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using MySocialApp.Api.Extensions;
using MySocialApp.Api.Hubs;
using MySocialApp.Api.Middleware;
using MySocialApp.Application.Options;
using MySocialApp.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Enable running as a Windows Service
builder.Host.UseWindowsService();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddSwaggerDocumentation();
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.PostConfigure<MediaStorageOptions>(options =>
{
    options.RootPath = MediaStorageOptions.ResolveRootPath(options.RootPath, builder.Environment.ContentRootPath);
});

// CORS - allow all origins with credentials for SignalR & REST clients in development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Seed database & apply migrations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MySocialAppDbContext>();
    context.Database.Migrate();
    SeedData.Initialize(context);
}

// Configure pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable Swagger UI for development and local testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MySocialApp API V1");
    c.RoutePrefix = string.Empty; // Swagger at root
});

app.UseCors("AllowAll");

var mediaOptions = builder.Configuration.GetSection(MediaStorageOptions.SectionName).Get<MediaStorageOptions>() ?? new MediaStorageOptions();
var storageRoot = MediaStorageOptions.ResolveRootPath(mediaOptions.RootPath, builder.Environment.ContentRootPath);
if (!Directory.Exists(storageRoot))
{
    Directory.CreateDirectory(storageRoot);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storageRoot),
    RequestPath = mediaOptions.BaseUrl.TrimEnd('/')
});

app.UseAuthentication();
// Personalized API responses must never be shared by browsers or reverse proxies.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && context.User.Identity?.IsAuthenticated == true)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Vary = "Authorization, Origin";
            return Task.CompletedTask;
        });
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();

// SignalR Realtime Hub
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
