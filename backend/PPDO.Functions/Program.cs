using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Application.Settings;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Infrastructure.Services;

// ── CORS allowlist (RAL-58) ───────────────────────────────────────────────────
// Only origins on this allowlist receive CORS headers; all others get none (the
// browser then blocks the cross-origin response). This replaces the previous
// origin echo-back, which — combined with Allow-Credentials — accepted credentialed
// requests from ANY site.
//
// Configure via the App Setting "Cors:AllowedOrigins" (exposed to the isolated
// worker as the env var Cors__AllowedOrigins), a comma-separated list of origins.
// Falls back to local dev origins (Next.js 3000 + SWA CLI 4280) when unset.
// ─────────────────────────────────────────────────────────────────────────────
string[] allowedOrigins =
    (Environment.GetEnvironmentVariable("Cors__AllowedOrigins")
     ?? Environment.GetEnvironmentVariable("Cors:AllowedOrigins")
     ?? "http://localhost:3000,http://localhost:4280")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

                // Only emit CORS headers for an allowlisted origin.
                if (!string.IsNullOrEmpty(origin)
                    && allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    http.Response.Headers["Access-Control-Allow-Origin"]      = origin;
                    http.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                    http.Response.Headers["Access-Control-Allow-Methods"]     =
                        "GET, POST, PUT, PATCH, DELETE, OPTIONS";
                    http.Response.Headers["Access-Control-Allow-Headers"]     =
                        "Content-Type, Authorization";
                    http.Response.Headers["Access-Control-Max-Age"]           = "86400";
                    // Response varies by Origin since the allowed value is request-specific.
                    http.Response.Headers["Vary"]                             = "Origin";
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

        // -- In-memory cache (login rate limiting, RAL-58) -------------------
        services.AddMemoryCache();

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
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IAipRepository, AipRepository>();
        services.AddScoped<ILdipRepository, LdipRepository>();
        services.AddScoped<IWfpRepository, WfpRepository>();
        services.AddScoped<IOfficeRepository, OfficeRepository>();
        services.AddScoped<IJwtMiddleware, JwtMiddleware>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IExcelService, ExcelService>();
        services.AddScoped<IWfpExcelService, ExcelService>();
        services.AddScoped<IWfpReportExcelService, WfpReportExcelService>();

        // NagerHolidayProvider uses a typed HttpClient. Timeout is short so a slow
        // Nager.Date response fails fast and falls back to static data or empty list.
        services.AddHttpClient<IHolidayProvider, NagerHolidayProvider>(c =>
            c.Timeout = TimeSpan.FromSeconds(3));

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
        services.AddScoped<IInventoryCleanupService, InventoryCleanupService>();
        services.AddScoped<IOfficeService, OfficeService>();
        services.AddScoped<IDivisionService, DivisionService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IFundingSourceService, FundingSourceService>();
        services.AddScoped<IPriceIndexService, PriceIndexService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IProcurementPresetRepository, ProcurementPresetRepository>();
        services.AddScoped<IRepository<ProcurementPresetItem>, Repository<ProcurementPresetItem>>();
        // RAL-164: scoped by-ids price-index lookup for ProcurementPresetService, distinct from
        // the generic IRepository<PriceIndexItem> still used by PriceIndexService itself.
        services.AddScoped<IPriceIndexItemRepository, PriceIndexItemRepository>();
        services.AddScoped<IProcurementPresetService, ProcurementPresetService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IBudgetPlanningDashboardService, BudgetPlanningDashboardService>();
        services.AddScoped<IWfpReportService, WfpReportService>();

        // -- v1.1.1 services -------------------------------------------------
        services.AddScoped<IAnnouncementService, AnnouncementService>();

        // -- Budget Planning services (RAL-64) --------------------------------
        services.AddScoped<IAipXlsmParser, AipXlsmParser>();
        services.AddScoped<ILdipXlsmParser, LdipXlsmParser>();
        services.AddScoped<ILdipService, LdipService>();
        services.AddScoped<IAipService, AipService>();
        services.AddScoped<IWfpService, WfpService>();

        // -- v1.2 Allocation (RAL-99) -----------------------------------------
        // RAL-163: BudgetCeiling/DivisionAllocation moved from generic IRepository<T> to
        // scoped feature repositories — see docs/Performance_Audit_2026-07-16.md Tier 1.
        services.AddScoped<IBudgetCeilingRepository, BudgetCeilingRepository>();
        services.AddScoped<IDivisionAllocationRepository, DivisionAllocationRepository>();
        services.AddScoped<IAllocationRepository, AllocationRepository>();
        services.AddScoped<IAllocationService, AllocationService>();

        // -- v1.4 WFP expenditure schema + computation pipeline (RAL-120) -----
        services.AddScoped<IRepository<WfpExpenditurePeriod>, Repository<WfpExpenditurePeriod>>();
        services.AddScoped<IRepository<WfpProcurementItem>, Repository<WfpProcurementItem>>();
        services.AddScoped<IWfpExpenditureRepository, WfpExpenditureRepository>();
        services.AddScoped<IWfpExpenditureService, WfpExpenditureService>();

        // -- v1.4 WFP ceiling monitoring + division-allocation ledger (RAL-122) --
        services.AddScoped<IWfpAllocationLedgerRepository, WfpAllocationLedgerRepository>();
        services.AddScoped<IWfpCeilingService, WfpCeilingService>();
    })
    .ConfigureLogging((context, logging) =>
    {
        // Local dev only (RAL-166 follow-up): surface EF Core's per-command SQL log
        // ("Executed DbCommand (Nms) [...]") in the func host console, the same trace shape
        // Application Insights Live Metrics already shows in prod. Lets you count exactly how
        // many DB round trips one action fires locally — start the backend, hit an endpoint,
        // then grep the console output for "Executed DbCommand" between the request's start
        // and finish. Gated on a blank connection string so this never changes what's captured
        // in prod (App Insights already surfaces these regardless of this filter).
        if (string.IsNullOrWhiteSpace(context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
    })
    .Build();

await host.RunAsync();
