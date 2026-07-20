using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Xml.Serialization;

namespace ParityBench.NET.Infrastructure;

internal sealed class NamespaceAgnosticXmlSerializerFactory
{
    private readonly ConcurrentDictionary<SerializerCacheKey, XmlSerializer> serializers = new ConcurrentDictionary<SerializerCacheKey, XmlSerializer>();

    public XmlSerializer GetSerializer(Type targetType, bool ignoreXmlNamespaces)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        return serializers.GetOrAdd(
            new SerializerCacheKey(targetType, ignoreXmlNamespaces),
            static key => key.IgnoreXmlNamespaces
                ? CreateNamespaceAgnosticSerializer(key.TargetType)
                : new XmlSerializer(key.TargetType));
    }

    private static XmlSerializer CreateNamespaceAgnosticSerializer(Type targetType)
    {
        XmlAttributeOverrides overrides = new XmlAttributeOverrides();
        ProcessType(targetType, overrides, new HashSet<Type>());
        return new XmlSerializer(targetType, overrides, Type.EmptyTypes, null, string.Empty);
    }

    private static void ProcessType(Type type, XmlAttributeOverrides overrides, HashSet<Type> visited)
    {
        type = UnwrapSerializableType(type);
        if (!ShouldProcess(type) || !visited.Add(type))
        {
            return;
        }

        XmlAttributes typeAttributes = new XmlAttributes();
        bool hasTypeOverride = false;

        XmlRootAttribute? rootAttribute = type.GetCustomAttribute<XmlRootAttribute>();
        if (rootAttribute is not null && !string.IsNullOrEmpty(rootAttribute.Namespace))
        {
            typeAttributes.XmlRoot = CloneWithoutNamespace(rootAttribute);
            hasTypeOverride = true;
        }

        XmlTypeAttribute? typeAttribute = type.GetCustomAttribute<XmlTypeAttribute>();
        if (typeAttribute is not null && !string.IsNullOrEmpty(typeAttribute.Namespace))
        {
            typeAttributes.XmlType = CloneWithoutNamespace(typeAttribute);
            hasTypeOverride = true;
        }

        if (hasTypeOverride)
        {
            overrides.Add(type, typeAttributes);
        }

        foreach (MemberInfo member in GetSerializableMembers(type))
        {
            ProcessMember(type, member, overrides, visited);
        }
    }

    private static void ProcessMember(Type declaringType, MemberInfo member, XmlAttributeOverrides overrides, HashSet<Type> visited)
    {
        XmlAttributes? memberAttributes = CreateMemberOverrides(member);
        if (memberAttributes is not null)
        {
            overrides.Add(declaringType, member.Name, memberAttributes);
        }

        Type memberType = GetMemberType(member);
        ProcessType(memberType, overrides, visited);

        foreach (Type extraType in GetAttributedTypes(member))
        {
            ProcessType(extraType, overrides, visited);
        }
    }

    private static XmlAttributes? CreateMemberOverrides(MemberInfo member)
    {
        List<XmlElementAttribute> sourceElementAttributes = member
            .GetCustomAttributes<XmlElementAttribute>()
            .ToList();
        Type memberType = GetMemberType(member);
        List<XmlElementAttribute> elementAttributes = sourceElementAttributes
            .Select(attribute => CloneWithoutNamespace(attribute, memberType))
            .ToList();
        XmlArrayAttribute? arrayAttribute = member
            .GetCustomAttribute<XmlArrayAttribute>();
        List<XmlArrayItemAttribute> sourceArrayItemAttributes = member
            .GetCustomAttributes<XmlArrayItemAttribute>()
            .ToList();
        List<XmlArrayItemAttribute> arrayItemAttributes = sourceArrayItemAttributes
            .Select(CloneWithoutNamespace)
            .ToList();
        XmlAttributeAttribute? attributeAttribute = member
            .GetCustomAttribute<XmlAttributeAttribute>();

        bool hasOrderOverride = sourceElementAttributes.Any(attribute => attribute.Order >= 0)
            || arrayAttribute?.Order >= 0;
        bool hasNamespaceOverride = elementAttributes.Any(HasEmptyNamespaceOverride)
            || (arrayAttribute is not null && !string.IsNullOrEmpty(arrayAttribute.Namespace))
            || arrayItemAttributes.Any(HasEmptyNamespaceOverride)
            || (attributeAttribute is not null && !string.IsNullOrEmpty(attributeAttribute.Namespace));

        if (!hasNamespaceOverride && !hasOrderOverride)
        {
            return null;
        }

        XmlAttributes attributes = new XmlAttributes();
        foreach (XmlElementAttribute elementAttribute in elementAttributes)
        {
            attributes.XmlElements.Add(elementAttribute);
        }

        if (arrayAttribute is not null)
        {
            attributes.XmlArray = CloneWithoutNamespace(arrayAttribute, memberType);
        }

        foreach (XmlArrayItemAttribute arrayItemAttribute in arrayItemAttributes)
        {
            attributes.XmlArrayItems.Add(arrayItemAttribute);
        }

        if (attributeAttribute is not null)
        {
            attributes.XmlAttribute = CloneWithoutNamespace(attributeAttribute);
        }

        XmlTextAttribute? textAttribute = member.GetCustomAttribute<XmlTextAttribute>();
        if (textAttribute is not null)
        {
            attributes.XmlText = textAttribute;
        }

        XmlIgnoreAttribute? ignoreAttribute = member.GetCustomAttribute<XmlIgnoreAttribute>();
        if (ignoreAttribute is not null)
        {
            attributes.XmlIgnore = true;
        }

        return attributes;
    }

    private static IEnumerable<MemberInfo> GetSerializableMembers(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (PropertyInfo property in type.GetProperties(Flags))
        {
            if (property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            {
                yield return property;
            }
        }

        foreach (FieldInfo field in type.GetFields(Flags))
        {
            yield return field;
        }
    }

    private static IEnumerable<Type> GetAttributedTypes(MemberInfo member)
    {
        foreach (XmlElementAttribute attribute in member.GetCustomAttributes<XmlElementAttribute>())
        {
            if (attribute.Type is not null)
            {
                yield return attribute.Type;
            }
        }

        foreach (XmlArrayItemAttribute attribute in member.GetCustomAttributes<XmlArrayItemAttribute>())
        {
            if (attribute.Type is not null)
            {
                yield return attribute.Type;
            }
        }
    }

    private static Type GetMemberType(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => typeof(object),
        };

    private static Type UnwrapSerializableType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType() ?? type;
        }

        if (type != typeof(string)
            && typeof(IEnumerable).IsAssignableFrom(type)
            && type.IsGenericType)
        {
            return type.GetGenericArguments()[0];
        }

        return Nullable.GetUnderlyingType(type) ?? type;
    }

    private static bool ShouldProcess(Type type) =>
        type != typeof(string)
        && !type.IsPrimitive
        && !type.IsEnum
        && type != typeof(decimal)
        && type != typeof(DateTime)
        && type != typeof(DateTimeOffset)
        && type != typeof(Guid)
        && type != typeof(TimeSpan)
        && type.Assembly != typeof(string).Assembly;

    private static bool HasEmptyNamespaceOverride(XmlElementAttribute attribute) =>
        attribute.Namespace == string.Empty;

    private static bool HasEmptyNamespaceOverride(XmlArrayItemAttribute attribute) =>
        attribute.Namespace == string.Empty;

    private static XmlRootAttribute CloneWithoutNamespace(XmlRootAttribute source) =>
        new XmlRootAttribute(source.ElementName)
        {
            DataType = source.DataType,
            IsNullable = source.IsNullable,
            Namespace = string.Empty,
        };

    private static XmlTypeAttribute CloneWithoutNamespace(XmlTypeAttribute source) =>
        new XmlTypeAttribute(source.TypeName)
        {
            AnonymousType = source.AnonymousType,
            IncludeInSchema = source.IncludeInSchema,
            Namespace = string.Empty,
        };

    private static XmlElementAttribute CloneWithoutNamespace(XmlElementAttribute source, Type memberType)
    {
        XmlElementAttribute clone = source.Type is null
            ? new XmlElementAttribute(source.ElementName)
            : new XmlElementAttribute(source.ElementName, source.Type);
        clone.DataType = source.DataType;
        clone.Form = source.Form;
        if (ShouldCopyIsNullable(source.IsNullable, memberType))
        {
            clone.IsNullable = source.IsNullable;
        }

        clone.Namespace = string.IsNullOrEmpty(source.Namespace) ? source.Namespace : string.Empty;

        return clone;
    }

    private static XmlArrayAttribute CloneWithoutNamespace(XmlArrayAttribute source, Type memberType)
    {
        XmlArrayAttribute clone = new XmlArrayAttribute(source.ElementName)
        {
            Form = source.Form,
            Namespace = string.IsNullOrEmpty(source.Namespace) ? source.Namespace : string.Empty,
        };
        if (ShouldCopyIsNullable(source.IsNullable, memberType))
        {
            clone.IsNullable = source.IsNullable;
        }

        return clone;
    }

    private static bool ShouldCopyIsNullable(bool isNullable, Type memberType) =>
        isNullable || Nullable.GetUnderlyingType(memberType) is null;

    private static XmlArrayItemAttribute CloneWithoutNamespace(XmlArrayItemAttribute source)
    {
        XmlArrayItemAttribute clone = source.Type is null
            ? new XmlArrayItemAttribute(source.ElementName)
            : new XmlArrayItemAttribute(source.ElementName, source.Type);
        clone.DataType = source.DataType;
        clone.Form = source.Form;
        clone.IsNullable = source.IsNullable;
        clone.Namespace = string.IsNullOrEmpty(source.Namespace) ? source.Namespace : string.Empty;
        clone.NestingLevel = source.NestingLevel;
        return clone;
    }

    private static XmlAttributeAttribute CloneWithoutNamespace(XmlAttributeAttribute source)
    {
        XmlAttributeAttribute clone = source.Type is null
            ? new XmlAttributeAttribute(source.AttributeName)
            : new XmlAttributeAttribute(source.AttributeName, source.Type);
        clone.DataType = source.DataType;
        clone.Form = source.Form;
        clone.Namespace = string.IsNullOrEmpty(source.Namespace) ? source.Namespace : string.Empty;
        return clone;
    }

    private readonly record struct SerializerCacheKey(Type TargetType, bool IgnoreXmlNamespaces);
}
