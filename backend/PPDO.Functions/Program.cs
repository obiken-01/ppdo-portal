using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDO.Application.Settings;
using PPDO.Infrastructure.Data;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // â”€â”€ JWT Options (Options pattern) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Binds Jwt__* keys from local.settings.json / Azure App Settings
        // to JwtSettings. Inject via IOptions<JwtSettings>.
        services.Configure<JwtSettings>(context.Configuration.GetSection("Jwt"));

        // â”€â”€ EF Core (Azure SQL) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        string connectionString = context.Configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "SqlConnectionString is not configured. " +
                "Add it to local.settings.json (local) or Azure App Settings (production).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        // â”€â”€ Application Insights â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Hooks into ILogger<T> automatically when APPLICATIONINSIGHTS_CONNECTION_STRING
        // is present. Leave the key blank in local.settings.json to skip telemetry locally.
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // â”€â”€ Infrastructure services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Uncomment each line as the implementation is added (RAL-33+):
        // services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        // services.AddScoped<IExcelService, ExcelService>();
        // services.AddScoped<ICurrentUserService, CurrentUserService>();

        // â”€â”€ Application services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Uncomment each line as the implementation is added (RAL-33+):
        // services.AddScoped<IAuthService, AuthService>();
        // services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
        // services.AddScoped<IDeliveryService, DeliveryService>();
        // services.AddScoped<IItemService, ItemService>();
        // services.AddScoped<IUserService, UserService>();
        // services.AddScoped<IPermissionService, PermissionService>();
    })
    .Build();

await host.RunAsync();
