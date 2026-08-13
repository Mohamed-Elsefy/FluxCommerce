using System.Text;
using System.Text.Json;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

public static class HttpExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static StringContent ToJsonContent(this object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> PostAsJsonAsync(this HttpClient client, string uri, object value) =>
        client.PostAsync(uri, value.ToJsonContent());

    public static Task<HttpResponseMessage> PutAsJsonAsync(this HttpClient client, string uri, object value) =>
        client.PutAsync(uri, value.ToJsonContent());

    public static Task<HttpResponseMessage> PatchAsJsonAsync(this HttpClient client, string uri, object value) =>
        client.PatchAsync(uri, value.ToJsonContent());

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }
}
