using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDO.Application.Services;
using PPDO.Application.Settings;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Infrastructure.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // -- JWT Options (Options pattern) ------------------------------------
        // Binds Jwt__* keys from local.settings.json / Azure App Settings
        // to JwtSettings. Inject via IOptions<JwtSettings>.
        services.Configure<JwtSettings>(context.Configuration.GetSection("Jwt"));

        // -- EF Core (Azure SQL) ---------------------------------------------
        string connectionString = context.Configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "SqlConnectionString is not configured. " +
                "Add it to local.settings.json (local) or Azure App Settings (production).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        // -- Application Insights --------------------------------------------
        // Hooks into ILogger<T> automatically when APPLICATIONINSIGHTS_CONNECTION_STRING
        // is present. Leave the key blank in local.settings.json to skip telemetry locally.
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // -- ASP.NET Core helpers --------------------------------------------
        // Required by JwtMiddleware and CurrentUserService to read/write HttpContext.
        services.AddHttpContextAccessor();

        // -- Infrastructure services (RAL-37 / RAL-38) -----------------------
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();
        services.AddScoped<IItemMasterRepository, ItemMasterRepository>();
        services.AddScoped<IJwtMiddleware, JwtMiddleware>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        // services.AddScoped<IExcelService, ExcelService>();           // RAL-46

        // -- Application services (RAL-38+) ----------------------------------
        services.AddScoped<IPermissionService, PermissionService>();
        // services.AddScoped<IAuthService, AuthService>();              // RAL-39
        // services.AddScoped<IUserService, UserService>();              // RAL-40
        // services.AddScoped<IItemService, ItemService>();              // RAL-47
        // services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();  // RAL-48
        // services.AddScoped<IDeliveryService, DeliveryService>();      // RAL-49
    })
    .Build();

await host.RunAsync();
