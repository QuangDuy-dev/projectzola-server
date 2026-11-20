using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MySocialApp.Api.Services;
using MySocialApp.Application.Interfaces;
using MySocialApp.Application.Options;
using MySocialApp.Application.Services;
using MySocialApp.Infrastructure.Data;
using MySocialApp.Infrastructure.Services;

namespace MySocialApp.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MySocialAppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MySocialAppDbContext>());

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidAudience = configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var tokenVersionClaim = context.Principal?.FindFirstValue("token_version");

                    if (!Guid.TryParse(userIdClaim, out var userId) ||
                        !int.TryParse(tokenVersionClaim, out var tokenVersion))
                    {
                        context.Fail("Invalid token claims.");
                        return;
                    }

                    var dbContext = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
                    var userState = await dbContext.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new { u.IsActive, u.TokenVersion })
                        .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                    if (userState == null || !userState.IsActive || userState.TokenVersion != tokenVersion)
                    {
                        context.Fail("Token has been revoked or user is inactive.");
                    }
                }
            };
        });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "MySocialApp API",
                Version = "v1",
                Description = "Social Network + Chat + E-Commerce API"
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer"),
                    new List<string>()
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.Configure<MediaStorageOptions>(configuration.GetSection(MediaStorageOptions.SectionName));
            services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
            services.Configure<TurnOptions>(configuration.GetSection(TurnOptions.SectionName));
        }

        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IFeedService, FeedService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IGroupChatService, GroupChatService>();
        services.AddScoped<IChatMediaService, ChatMediaService>();
        services.AddScoped<IChatInteractionService, ChatInteractionService>();
        services.AddScoped<IPresenceQueryService, PresenceQueryService>();
        services.AddScoped<ICallService, CallService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRevenueService, RevenueService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IShipperService, ShipperService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IFileValidator, FileValidator>();
        services.AddScoped<IMediaProcessingService, MediaProcessingService>();
        services.AddScoped<IImageProcessingService, ImageProcessingService>();
        services.AddScoped<IMediaStorageService, LocalMediaStorageService>();

        // Realtime notification sender
        services.AddScoped<IRealtimeNotificationSender, SignalRNotificationSender>();

        // Presence service is in-memory and shared across connections
        services.AddSingleton<IUserPresenceService, InMemoryUserPresenceService>();

        // Chatbot & Ollama services
        services.AddMemoryCache();
        if (configuration != null)
        {
            services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        }

        services.AddHttpClient<IOllamaClient, OllamaClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddSingleton<IAppKnowledgeService, AppKnowledgeService>();
        services.AddScoped<IChatSessionService, ChatSessionService>();
        services.AddScoped<IChatbotService, ChatbotService>();

        // SignalR realtime service
        services.AddSignalR();

        return services;
    }
}
