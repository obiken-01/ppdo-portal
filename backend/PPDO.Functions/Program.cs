using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Application.Settings;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Infrastructure.Services;

// ── CORS allowed origins ──────────────────────────────────────────────────────
// Local dev: Next.js (3000) + SWA CLI (4280).
// Production: set AllowedOrigins in Azure Function App Settings.
// ─────────────────────────────────────────────────────────────────────────────

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        // CORS middleware for the isolated worker model.
        // worker.Use takes Func<FunctionExecutionDelegate, FunctionExecutionDelegate>.
        // GetHttpContext() is available via the AspNetCore extension package and
        // returns the ASP.NET Core HttpContext for HTTP-triggered functions.
        worker.Use(next => async (FunctionContext functionCtx) =>
        {
            HttpContext? http = functionCtx.GetHttpContext();

            if (http is not null)
            {
                string? origin = http.Request.Headers.Origin.FirstOrDefault();

                if (!string.IsNullOrEmpty(origin))
                {
                    http.Response.Headers["Access-Control-Allow-Origin"]      = origin;
                    http.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                    http.Response.Headers["Access-Control-Allow-Methods"]     =
                        "GET, POST, PUT, PATCH, DELETE, OPTIONS";
                    http.Response.Headers["Access-Control-Allow-Headers"]     =
                        "Content-Type, Authorization";
                    http.Response.Headers["Access-Control-Max-Age"]           = "86400";
                }

                // Answer OPTIONS preflight immediately — no function handler needed.
                if (HttpMethods.IsOptions(http.Request.Method))
                {
                    http.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }
            }

            await next(functionCtx);
        });
    })
    .ConfigureServices((context, services) =>
    {
        // -- JWT Options (Options pattern) ------------------------------------
        services.Configure<JwtSettings>(context.Configuration.GetSection("Jwt"));

        // -- EF Core (Azure SQL) ---------------------------------------------
        string connectionString = context.Configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "SqlConnectionString is not configured. " +
                "Add it to local.settings.json (local) or Azure App Settings (production).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        // -- Application Insights --------------------------------------------
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // -- ASP.NET Core helpers --------------------------------------------
        services.AddHttpContextAccessor();

        // -- Scoped request context ------------------------------------------
        services.AddScoped<CallerContext>();

        // -- Infrastructure services -----------------------------------------
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IItemMasterRepository, ItemMasterRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ICalendarEventRepository, CalendarEventRepository>();
        services.AddScoped<IJwtMiddleware, JwtMiddleware>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IExcelService, ExcelService>();

        // NagerHolidayProvider uses a singleton HttpClient via IHttpClientFactory.
        services.AddHttpClient<IHolidayProvider, NagerHolidayProvider>();

        // -- Application services --------------------------------------------
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IResourceLinkService, ResourceLinkService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
        services.AddScoped<IDeliveryService, DeliveryService>();
        services.AddScoped<IDistributionService, DistributionService>();
        services.AddScoped<IPRReportService, PRReportService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IOfficeService, OfficeService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IFundingSourceService, FundingSourceService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IBudgetPlanningDashboardService, BudgetPlanningDashboardService>();
    })
    .Build();

await host.RunAsync();
