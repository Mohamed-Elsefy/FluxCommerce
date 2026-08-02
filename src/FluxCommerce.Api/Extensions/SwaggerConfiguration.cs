using Microsoft.OpenApi;

namespace FluxCommerce.Api.Extensions;

public static class SwaggerConfiguration
{
    private const string ApiVersion = "v1";

    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(ApiVersion, new OpenApiInfo
            {
                Title = "FluxCommerce API",
                Description = "Enterprise Commerce & Order Management Platform — Modular Monolith API.",
                Version = ApiVersion
            });

            var xmlFile = $"{typeof(SwaggerConfiguration).Assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT access token. All secured endpoints require the 'Authorization: Bearer <token>' header."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = []
            });
        });

        return services;
    }
}
