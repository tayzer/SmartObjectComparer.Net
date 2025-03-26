using System.Xml.Serialization;

namespace ComparisonTool.Core.V2;

public class XmlSerializerFactory
{
    private readonly Dictionary<Type, Func<XmlSerializer>> _serializerFactories = new();

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
        _serializerFactories[typeof(T)] = factory;
    }

    public XmlSerializer GetSerializer<T>()
    {
        if (_serializerFactories.TryGetValue(typeof(T), out var factory))
        {
            return factory();
        }

        // Default serializer for types without special requirements
        return new XmlSerializer(typeof(T));
    }

    public XmlSerializer GetSerializer(Type type)
    {
        if (_serializerFactories.TryGetValue(type, out var factory))
        {
            return factory();
        }

        // Default serializer
        return new XmlSerializer(type);
    }
}