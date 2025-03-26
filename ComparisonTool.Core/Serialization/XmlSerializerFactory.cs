using System.Xml.Serialization;
using ComparisonTool.Core.Models;

namespace ComparisonTool.Core.Serialization;

public class XmlSerializerFactory
{
    private readonly Dictionary<Type, Func<XmlSerializer>> serializerFactories = new();

    public XmlSerializerFactory()
    {
        // todo: we should ideally register this with DI.
        RegisterType<SoapEnvelope>(() => new XmlSerializer(
            typeof(SoapEnvelope),
            new XmlRootAttribute
            {
                ElementName = "Envelope",
                Namespace = "http://schemas.xmlsoap.org/soap/envelope/"
            }));
    }

    public void RegisterType<T>(Func<XmlSerializer> factory)
    {
        serializerFactories[typeof(T)] = factory;
    }

    public XmlSerializer GetSerializer<T>()
    {
        if (serializerFactories.TryGetValue(typeof(T), out var factory))
        {
            return factory();
        }

        return new XmlSerializer(typeof(T));
    }

    public XmlSerializer GetSerializer(Type type)
    {
        if (serializerFactories.TryGetValue(type, out var factory))
        {
            return factory();
        }

        return new XmlSerializer(type);
    }
}