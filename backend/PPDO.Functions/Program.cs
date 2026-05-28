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
        // ── JWT Options (Options pattern) ─────────────────────────────────────
        // Binds Jwt__* keys from local.settings.json / Azure App Settings
        // to JwtSettings. Inject via IOptions<JwtSettings>.
        services.Configure<JwtSettings>(context.Configuration.GetSection("Jwt"));

        // ── EF Core (Azure SQL) ───────────────────────────────────────────────
        string connectionString = context.Configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "SqlConnectionString is not configured. " +
                "Add it to local.settings.json (local) or Azure App Settings (production).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        // ── Application Insights ──────────────────────────────────────────────
        // Hooks into ILogger<T> automatically when APPLICATIONINSIGHTS_CONNECTION_STRING
        // is present. Leave the key blank in local.settings.json to skip telemetry locally.
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // ── Infrastructure services ───────────────────────────────────────────
        // Uncomment each line as the implementation is added (RAL-30+):
        // services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        // services.AddScoped<IExcelService, ExcelService>();
        // services.AddScoped<ICurrentUserService, CurrentUserService>();

        // ── Application services ──────────────────────────────────────────────
        // Uncomment each line as the implementation is added (RAL-30+):
        // services.AddScoped<IAuthService, AuthService>();
        // services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
        // services.AddScoped<IDeliveryService, DeliveryService>();
        // services.AddScoped<IItemService, ItemService>();
        // services.AddScoped<IUserService, UserService>();
        // services.AddScoped<IPermissionService, PermissionService>();
    })
    .Build();

await host.RunAsync();
