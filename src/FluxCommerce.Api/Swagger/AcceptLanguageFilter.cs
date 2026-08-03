using System.Text.Json.Nodes;
using FluxCommerce.Api.Extensions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FluxCommerce.Api.Swagger;

public sealed class AcceptLanguageFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters?.Any(p =>
                string.Equals(p.Name, "Accept-Language", StringComparison.OrdinalIgnoreCase)
                && p.In == ParameterLocation.Header) == true)
        {
            return;
        }

        operation.Parameters ??= [];

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept-Language",
            In = ParameterLocation.Header,
            Description =
                "The preferred response language. "
                + $"Supported cultures: {string.Join(", ", LocalizationConfiguration.SupportedCultures)}. "
                + $"Default: {LocalizationConfiguration.DefaultCulture}.",
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = LocalizationConfiguration.SupportedCultures
                    .Select(culture => (JsonNode)JsonValue.Create(culture)!)
                    .ToList(),
                Default = JsonValue.Create(LocalizationConfiguration.DefaultCulture)
            }
        });
    }
}
