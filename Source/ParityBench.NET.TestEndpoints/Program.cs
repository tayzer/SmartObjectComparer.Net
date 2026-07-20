using ParityBench.NET.TestEndpoints.ClientCustomerLookup;
using ParityBench.NET.TestEndpoints.ConsumerReports;
using ParityBench.NET.TestEndpoints.SampleCustomerLookup;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

WebApplication app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "ParityBench.NET.TestEndpoints",
}));

app.MapConsumerReportEndpoints();
app.MapSampleCustomerLookupEndpoints();
app.MapClientCustomerLookupEndpoints();

app.Run();

public partial class Program
{
}
