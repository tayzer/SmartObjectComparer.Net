using System.Text;
using System.Text.Json;

namespace ParityBench.NET.TestEndpoints.ConsumerReports;

public static class ConsumerReportEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void MapConsumerReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/consumer-report");
        group.MapPost("/soap/a", (Delegate)((HttpContext context) => HandleSoapAsync(context, EndpointVariant.A)));
        group.MapPost("/soap/b", (Delegate)((HttpContext context) => HandleSoapAsync(context, EndpointVariant.B)));
        group.MapPost("/json/a", (Delegate)((HttpContext context) => HandleJsonAsync(context, EndpointVariant.A)));
        group.MapPost("/json/b", (Delegate)((HttpContext context) => HandleJsonAsync(context, EndpointVariant.B)));
    }

    private static async Task<IResult> HandleSoapAsync(HttpContext context, EndpointVariant variant)
    {
        using StreamReader reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        string requestBody = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        ConsumerReportRequest request = ConsumerReportSoapSerializer.ReadRequest(requestBody);
        ConsumerReportResponse response = ConsumerReportFixtures.CreateResponse(variant, request);

        context.Response.Headers["X-Provider-TraceId"] = response.ProviderTraceId;
        return Results.Text(
            ConsumerReportSoapSerializer.WriteResponse(response),
            "application/xml",
            Encoding.UTF8);
    }

    private static async Task<IResult> HandleJsonAsync(HttpContext context, EndpointVariant variant)
    {
        ConsumerReportJsonRequest? requestBody = await JsonSerializer
            .DeserializeAsync<ConsumerReportJsonRequest>(context.Request.Body, JsonOptions, context.RequestAborted)
            .ConfigureAwait(false);
        ConsumerReportRequest request = ConsumerReportRequest.FromJson(requestBody);
        ConsumerReportResponse response = ConsumerReportFixtures.CreateResponse(variant, request);

        context.Response.Headers["X-Provider-TraceId"] = response.ProviderTraceId;
        return Results.Json(response, JsonOptions, contentType: "application/json");
    }
}
