using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace IdentityService.Tests;

/// <summary>
/// Stands in for Google in tests: answers with the first rule whose test matches the request, else 503.
/// Every request (with its body) is recorded so a test can check what we sent.
/// </summary>
public class FakeGoogleHandler : HttpMessageHandler
{
    public record Sent(HttpMethod Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);

    private readonly ConcurrentQueue<(Func<Sent, bool> When, HttpStatusCode Status, string Json)> _rules = new();
    public ConcurrentQueue<Sent> Requests { get; } = new();

    public FakeGoogleHandler On(Func<Sent, bool> when, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _rules.Enqueue((when, status, json));
        return this;
    }

    public int Count(Func<Sent, bool> match) => Requests.Count(match);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var sent = new Sent(request.Method, request.RequestUri!.ToString(), body, headers);
        Requests.Enqueue(sent);

        foreach (var rule in _rules)
            if (rule.When(sent))
                return new HttpResponseMessage(rule.Status) { Content = new StringContent(rule.Json, Encoding.UTF8, "application/json") };
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error":{"code":503,"status":"UNAVAILABLE"}}""", Encoding.UTF8, "application/json"),
        };
    }

    public static bool IsAutocomplete(Sent s) => s.Url.Contains("places:autocomplete");
    public static bool IsDetails(Sent s) => s.Url.Contains("/places/") && !IsAutocomplete(s);
    public static bool IsReverse(Sent s) => s.Url.Contains("geocode/json");
}

/// <summary>Trimmed copies of real Google responses.</summary>
public static class GoogleSamples
{
    public const string KukatpallyId = "ChIJ-test-kukatpally";
    public const string DublinId = "ChIJ-test-main-street-dublin";
    public const string LondonId = "ChIJ-test-london";

    public const string AutocompleteKukatpally = """
        {"suggestions":[
          {"placePrediction":{"place":"places/ChIJ-test-kukatpally","placeId":"ChIJ-test-kukatpally",
            "text":{"text":"Kukatpally, Hyderabad, Telangana, India"},
            "structuredFormat":{"mainText":{"text":"Kukatpally","matches":[{"endOffset":10}]},"secondaryText":{"text":"Hyderabad, Telangana, India"}},
            "types":["sublocality_level_1","sublocality","political","geocode"]}},
          {"placePrediction":{"place":"places/ChIJ-test-kphb","placeId":"ChIJ-test-kphb",
            "text":{"text":"KPHB Colony, Kukatpally, Hyderabad, Telangana, India"},
            "types":["sublocality_level_2","political"]}},
          {"queryPrediction":{"text":{"text":"kukatpally bus stop"}}}
        ]}
        """;

    public const string DetailsKukatpally = """
        {"id":"ChIJ-test-kukatpally","formattedAddress":"Kukatpally, Hyderabad, Telangana 500072, India",
         "location":{"latitude":17.4947934,"longitude":78.3996441},
         "addressComponents":[
           {"longText":"Kukatpally","shortText":"Kukatpally","types":["sublocality_level_1","sublocality","political"],"languageCode":"en"},
           {"longText":"Hyderabad","shortText":"Hyderabad","types":["locality","political"],"languageCode":"en"},
           {"longText":"Medchal-Malkajgiri","shortText":"Medchal-Malkajgiri","types":["administrative_area_level_3","political"],"languageCode":"en"},
           {"longText":"Telangana","shortText":"TS","types":["administrative_area_level_1","political"],"languageCode":"en"},
           {"longText":"India","shortText":"IN","types":["country","political"],"languageCode":"en"},
           {"longText":"500072","shortText":"500072","types":["postal_code"],"languageCode":"en"}],
         "displayName":{"text":"Kukatpally","languageCode":"en"}}
        """;

    public const string DetailsMainStreetDublin = """
        {"id":"ChIJ-test-main-street-dublin","formattedAddress":"12 Main Street, Blanchardstown, Dublin 15, D15 X2Y3, Ireland",
         "location":{"latitude":53.3874,"longitude":-6.3776},
         "addressComponents":[
           {"longText":"12","shortText":"12","types":["street_number"]},
           {"longText":"Main Street","shortText":"Main St","types":["route"]},
           {"longText":"Blanchardstown","shortText":"Blanchardstown","types":["sublocality_level_1","sublocality","political"]},
           {"longText":"Dublin","shortText":"Dublin","types":["locality","political"]},
           {"longText":"County Dublin","shortText":"Co. Dublin","types":["administrative_area_level_1","political"]},
           {"longText":"Ireland","shortText":"IE","types":["country","political"]},
           {"longText":"D15 X2Y3","shortText":"D15 X2Y3","types":["postal_code"]}],
         "displayName":{"text":"12 Main Street"}}
        """;

    public const string DetailsLondon = """
        {"id":"ChIJ-test-london","formattedAddress":"London, UK","location":{"latitude":51.5072,"longitude":-0.1276},
         "addressComponents":[
           {"longText":"London","shortText":"London","types":["locality","political"]},
           {"longText":"England","shortText":"England","types":["administrative_area_level_1","political"]},
           {"longText":"United Kingdom","shortText":"GB","types":["country","political"]}],
         "displayName":{"text":"London"}}
        """;

    /// <summary>Houston downtown: a plus code first (skipped), then the street address.</summary>
    public const string ReverseHouston = """
        {"plus_code":{"global_code":"76X6QJ6J+5W"},"results":[
          {"address_components":[{"long_name":"QJ6J+5W","short_name":"QJ6J+5W","types":["plus_code"]}],
           "formatted_address":"QJ6J+5W Houston, TX, USA","geometry":{"location":{"lat":29.7604,"lng":-95.3698}},
           "place_id":"GhIJ-plus","types":["plus_code"]},
          {"address_components":[
             {"long_name":"901","short_name":"901","types":["street_number"]},
             {"long_name":"Bagby Street","short_name":"Bagby St","types":["route"]},
             {"long_name":"Downtown","short_name":"Downtown","types":["neighborhood","political"]},
             {"long_name":"Houston","short_name":"Houston","types":["locality","political"]},
             {"long_name":"Harris County","short_name":"Harris County","types":["administrative_area_level_2","political"]},
             {"long_name":"Texas","short_name":"TX","types":["administrative_area_level_1","political"]},
             {"long_name":"United States","short_name":"US","types":["country","political"]},
             {"long_name":"77002","short_name":"77002","types":["postal_code"]}],
           "formatted_address":"901 Bagby St, Houston, TX 77002, USA","geometry":{"location":{"lat":29.76045,"lng":-95.36985}},
           "place_id":"ChIJ-test-houston","types":["street_address"]},
          {"address_components":[
             {"long_name":"Houston","short_name":"Houston","types":["locality","political"]},
             {"long_name":"Texas","short_name":"TX","types":["administrative_area_level_1","political"]},
             {"long_name":"United States","short_name":"US","types":["country","political"]}],
           "formatted_address":"Houston, TX, USA","geometry":{"location":{"lat":29.76,"lng":-95.37}},
           "place_id":"ChIJ-test-houston-city","types":["locality","political"]}
        ],"status":"OK"}
        """;

    public const string ReverseKukatpally = """
        {"results":[
          {"address_components":[
             {"long_name":"Road Number 1","short_name":"Rd Number 1","types":["route"]},
             {"long_name":"Kukatpally Housing Board Colony","short_name":"KPHB","types":["sublocality_level_2","sublocality","political"]},
             {"long_name":"Kukatpally","short_name":"Kukatpally","types":["sublocality_level_1","sublocality","political"]},
             {"long_name":"Hyderabad","short_name":"Hyderabad","types":["locality","political"]},
             {"long_name":"Telangana","short_name":"TS","types":["administrative_area_level_1","political"]},
             {"long_name":"India","short_name":"IN","types":["country","political"]},
             {"long_name":"500072","short_name":"500072","types":["postal_code"]}],
           "formatted_address":"Road Number 1, KPHB Colony, Kukatpally, Hyderabad, Telangana 500072, India",
           "geometry":{"location":{"lat":17.4949,"lng":78.3997}},"place_id":"ChIJ-test-kphb-road","types":["route"]}
        ],"status":"OK"}
        """;

    public const string PermissionDenied = """{"error":{"code":403,"message":"API key not valid.","status":"PERMISSION_DENIED"}}""";
}
