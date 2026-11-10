using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DeskDashboard.Core.Location;

public sealed record LocationSearchResult(
    string Name,
    string? Admin1,
    string Country,
    double Latitude,
    double Longitude,
    string? TimeZoneId,
    string Label);

public sealed class GeocodingClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<LocationSearchResult>> SearchAsync(
        string query,
        CancellationToken ct)
    {
        var url = $"v1/search?name={Uri.EscapeDataString(query)}&count=5&language=en&format=json";
        var response = await httpClient.GetFromJsonAsync<GeocodingResponse>(url, ct);

        if (response?.Results is null)
        {
            return [];
        }

        return response.Results
            .Where(result => result.Latitude.HasValue && result.Longitude.HasValue)
            .Select(result => new LocationSearchResult(
                result.Name ?? "Unknown",
                result.Admin1,
                result.Country ?? "",
                result.Latitude!.Value,
                result.Longitude!.Value,
                result.Timezone,
                string.Join(", ", new[] { result.Name, result.Admin1, result.Country }
                    .Where(part => !string.IsNullOrWhiteSpace(part)))))
            .ToList();
    }

    private sealed class GeocodingResponse
    {
        [JsonPropertyName("results")]
        public List<GeocodingResult>? Results { get; set; }
    }

    private sealed class GeocodingResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }
    }
}
