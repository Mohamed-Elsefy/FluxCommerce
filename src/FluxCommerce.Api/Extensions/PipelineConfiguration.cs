namespace FluxCommerce.Api.Extensions;

public static class PipelineConfiguration
{
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapHealthChecks("/health");

        app.MapControllers();

        return app;
    }
}
