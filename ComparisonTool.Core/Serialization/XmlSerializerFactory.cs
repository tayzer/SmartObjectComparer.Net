using System.Xml.Serialization;
using ComparisonTool.Core.Models;

namespace ComparisonTool.Core.Serialization;

public class XmlSerializerFactory
{
    private readonly Dictionary<Type, Func<XmlSerializer>> serializerFactories = new();

    public XmlSerializerFactory()
    {
        // todo: we should ideally register this with DI.
        RegisterType<ComplexOrderResponse>(() => new XmlSerializer(
            typeof(ComplexOrderResponse),
            new XmlRootAttribute
            {
                ElementName = "OrderManagementResponse",
                Namespace = ""
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