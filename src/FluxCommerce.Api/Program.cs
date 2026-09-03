using FluxCommerce.Api.Extensions;
using FluxCommerce.Modules.Identity.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();

builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
    await seeder.SeedAsync();
}

app.ConfigurePipeline();

app.Run();

public partial class Program { }
