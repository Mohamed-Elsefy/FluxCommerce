using FluxCommerce.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();

builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.ConfigurePipeline();

app.Run();

public partial class Program { }
