namespace FluxCommerce.BuildingBlocks.Web.Localization;

public interface IErrorCodeLocalizer
{
    string Localize(string code, string fallback);
}