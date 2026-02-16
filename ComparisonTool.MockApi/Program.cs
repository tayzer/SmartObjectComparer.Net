using System.Text;
using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Models;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/mock/a", async (HttpRequest request) =>
{
    //return Results.StatusCode(statusCode:StatusCodes.Status500InternalServerError);

    var body = await new StreamReader(request.Body).ReadToEndAsync();
    var contentType = request.ContentType ?? "text/plain";

    return BuildResponse("A", body, contentType, includeDiff: false);
});

app.MapPost("/api/mock/b", async (HttpRequest request) =>
{
    //return Results.BadRequest(new { error = "Simulated error response for testing" });

    var body = await new StreamReader(request.Body).ReadToEndAsync();
    var contentType = request.ContentType ?? "text/plain";

    return BuildResponse("B", body, contentType, includeDiff: true);
});

app.Run();

static IResult BuildResponse(string source, string body, string contentType, bool includeDiff)
{
    var response = BuildComplexOrderResponse(source, body, includeDiff);

    if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
    {
        var xml = SerializeToXml(response);
        return Results.Text(xml, "application/xml");
    }

    return Results.Json(response);
}

static ComplexOrderResponse BuildComplexOrderResponse(string source, string body, bool includeDiff)
{
    var response = new ComplexOrderResponse
    {
        RequestId = $"{source}-{Guid.NewGuid()}",
        Timestamp = DateTime.UtcNow,
        ApiVersion = includeDiff ? "2.1.5" : "2.1.4",
        ProcessingTime = TimeSpan.FromMilliseconds(includeDiff ? 185 : 120),
    };

    response.Metadata.Region = includeDiff ? "US-WEST-2" : "US-EAST-1";
    response.Metadata.Environment = includeDiff ? "Staging" : "Production";
    response.Metadata.ServerInfo.ServerId = $"{Environment.MachineName}-{source}";
    response.Metadata.ServerInfo.DeploymentVersion = includeDiff ? "v2.1.5-rc.1" : "v2.1.4-hotfix.3";
    response.Metadata.Performance.DatabaseQueryTime = TimeSpan.FromMilliseconds(includeDiff ? 42 : 18);
    response.Metadata.Performance.ExternalApiCalls = includeDiff ? 5 : 2;
    response.Metadata.Performance.CacheHitRatio = includeDiff ? 0.74 : 0.92;

    response.OrderData.OrderId = includeDiff ? "ORDER-ALT-001" : "ORDER-001";
    response.OrderData.OrderNumber = includeDiff ? "ALT-10001" : "10001";
    response.OrderData.SourceSystem = source;
    response.OrderData.Status = includeDiff ? OrderStatus.Processing : OrderStatus.Confirmed;

    response.OrderData.Customer.CustomerId = includeDiff ? "CUST-ALT-001" : "CUST-001";
    response.OrderData.Customer.Profile.FirstName = includeDiff ? "Alex" : "Jamie";
    response.OrderData.Customer.Profile.LastName = includeDiff ? "Smith" : "Taylor";
    response.OrderData.Customer.Profile.Email = includeDiff ? "alex.smith@example.com" : "jamie.taylor@example.com";
    response.OrderData.Customer.Profile.Phone = includeDiff ? "+1-555-0144" : "+1-555-0123";

    response.OrderData.Items.Add(new OrderItem
    {
        ItemId = includeDiff ? "ITEM-ALT-01" : "ITEM-01",
        Quantity = includeDiff ? 2 : 1,
        Product = new Product
        {
            ProductId = includeDiff ? "PROD-ALT-01" : "PROD-01",
            SKU = includeDiff ? "SKU-ALT-100" : "SKU-100",
            Name = includeDiff ? "Contoso Trail Backpack" : "Contoso Daypack",
            Description = includeDiff ? "Trail-ready backpack" : "Lightweight daypack",
        },
        Pricing = new ItemPricing
        {
            UnitPrice = includeDiff ? 129.99m : 89.99m,
            DiscountAmount = includeDiff ? 10.00m : 0m,
            TaxAmount = includeDiff ? 8.45m : 6.20m,
            TotalPrice = includeDiff ? 128.44m : 96.19m,
        },
    });

    if (!string.IsNullOrWhiteSpace(body))
    {
        response.ValidationMessages.Add(new ValidationMessage
        {
            MessageId = "BODY-01",
            Severity = MessageSeverity.Info,
            Code = "PayloadCaptured",
            Message = "Request body captured for comparison",
            Field = "RawBody",
            Timestamp = DateTime.UtcNow,
        });
    }

    return response;
}

static string SerializeToXml<T>(T value)
{
    var serializer = new XmlSerializer(typeof(T));
    var namespaces = new XmlSerializerNamespaces();
    namespaces.Add(string.Empty, string.Empty);

    var settings = new XmlWriterSettings
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        OmitXmlDeclaration = false
    };

    using var writer = new Utf8StringWriter();
    using var xmlWriter = XmlWriter.Create(writer, settings);
    serializer.Serialize(xmlWriter, value, namespaces);
    return writer.ToString();
}

sealed class Utf8StringWriter : StringWriter
{
    public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
