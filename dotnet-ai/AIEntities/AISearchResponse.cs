using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace AIEntities;

public class AISearchResponse
{
    [JsonPropertyName("id")]
    public string Response { get; set; }

    [JsonPropertyName("products")]
    public List<DataEntities.Product>? Products { get; set; }

}


[JsonSerializable(typeof(List<AISearchResponse>))]
public sealed partial class AISearchResponseSerializerContext : JsonSerializerContext
{
}