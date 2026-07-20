using ParityBench.NET.Application.Requests;

namespace ParityBench.NET.Infrastructure;

public static class ConsumerReportFixtureResponseModelRegistration
{
    public const string SoapModelName = "ConsumerReportSoapResponseEnvelope";
    public const string JsonModelName = "ConsumerReportJsonResponse";

    public static void Register(IResponseModelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register<ConsumerReportSoapResponseEnvelope>(SoapModelName);
        registry.Register<ConsumerReportJsonResponse>(JsonModelName);
    }
}
