using System.Text;
using System.Text.Json;

namespace ParityBench.NET.TestEndpoints.SampleCustomerLookup;

public static class SampleCustomerLookupEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static void MapSampleCustomerLookupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/sample/customer-lookup");
        group.MapPost("/soap/a", (Delegate)((HttpContext context) => HandleSoapAsync(context, EndpointVariant.A)));
        group.MapPost("/soap/b", (Delegate)((HttpContext context) => HandleSoapAsync(context, EndpointVariant.B)));
        group.MapPost("/json/a", (Delegate)((HttpContext context) => HandleJsonAsync(context, EndpointVariant.A)));
        group.MapPost("/json/b", (Delegate)((HttpContext context) => HandleJsonAsync(context, EndpointVariant.B)));
    }

    private static async Task<IResult> HandleSoapAsync(HttpContext context, EndpointVariant variant)
    {
        using StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        string requestBody = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        SampleCustomerLookupRequest request = SampleCustomerLookupSoapSerializer.ReadRequest(requestBody);
        SampleCustomerLookupSoapResponse response = SampleCustomerLookupFixtures.CreateSoapResponse(variant, request);

        return Results.Text(
            SampleCustomerLookupSoapSerializer.WriteResponse(response),
            "application/xml",
            Encoding.UTF8);
    }

    private static async Task<IResult> HandleJsonAsync(HttpContext context, EndpointVariant variant)
    {
        SampleCustomerLookupJsonRequest? request = await JsonSerializer
            .DeserializeAsync<SampleCustomerLookupJsonRequest>(context.Request.Body, JsonOptions, context.RequestAborted)
            .ConfigureAwait(false);

        if (request is null)
        {
            throw new InvalidOperationException("Sample customer lookup JSON request body was empty.");
        }

        SampleCustomerLookupJsonResponse response = SampleCustomerLookupFixtures.CreateJsonResponse(variant, request);
        return Results.Json(response, JsonOptions, contentType: "application/json");
    }
}
