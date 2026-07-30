using FluxCommerce.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.ConfigurePipeline();

app.Run();
