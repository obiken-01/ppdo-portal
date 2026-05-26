using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PPDO.Infrastructure.Data;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        string connectionString = context.Configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "SqlConnectionString is not configured. " +
                "Add it to local.settings.json (local) or Azure App Settings (production).");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));
    })
    .Build();

await host.RunAsync();
