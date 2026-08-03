using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxCommerce.Api.Swagger;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace FluxCommerce.IntegrationTests;

[Collection(IdentityTestCollection.Name)]
public class SwaggerLocalizationTests
{
    private readonly IdentityWebApplicationFactory _factory;

    public SwaggerLocalizationTests(IdentityWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerDocument_ExposesAcceptLanguageHeader_WithSupportedCultures()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var loginOperation = json.RootElement
            .GetProperty("paths")
            .GetProperty("/auth/login")
            .GetProperty("post");

        var parameters = loginOperation.GetProperty("parameters");
        var acceptLanguage = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "Accept-Language");

        Assert.Equal("header", acceptLanguage.GetProperty("in").GetString());

        var schema = acceptLanguage.GetProperty("schema");
        var cultures = schema
            .GetProperty("enum")
            .EnumerateArray()
            .Select(culture => culture.GetString())
            .ToList();

        Assert.Equal(["en", "fr", "de", "es", "ar"], cultures);
        Assert.Equal("en", schema.GetProperty("default").GetString());
    }

    [Fact]
    public void AcceptLanguageFilter_AddsHeaderParameter_ToOperation()
    {
        var filter = new AcceptLanguageFilter();
        var operation = new OpenApiOperation();

        filter.Apply(operation, CreateContext());

        var parameter = Assert.Single(operation.Parameters!);
        Assert.Equal("Accept-Language", parameter.Name);
        Assert.Equal(ParameterLocation.Header, parameter.In);
        Assert.False(parameter.Required);

        var cultures = parameter.Schema!.Enum!
            .Cast<JsonNode>()
            .Select(node => node.GetValue<string>())
            .ToList();

        Assert.Equal(["en", "fr", "de", "es", "ar"], cultures);
        Assert.Equal("en", parameter.Schema.Default!.GetValue<string>());
    }

    [Fact]
    public void AcceptLanguageFilter_DoesNotDuplicate_ExistingHeaderParameter()
    {
        var filter = new AcceptLanguageFilter();
        var operation = new OpenApiOperation
        {
            Parameters = [
                new OpenApiParameter
                {
                    Name = "Accept-Language",
                    In = ParameterLocation.Header,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            ]
        };

        filter.Apply(operation, CreateContext());

        Assert.Single(operation.Parameters!);
    }

    private static OperationFilterContext CreateContext() =>
        new(
            new ApiDescription(),
            default!,
            new SchemaRepository(),
            new OpenApiDocument(),
            typeof(SwaggerLocalizationTests).GetMethod(nameof(CreateContext))!);
}
