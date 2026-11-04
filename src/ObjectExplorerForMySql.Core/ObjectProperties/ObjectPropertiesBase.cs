using System;
using System.ComponentModel;

namespace ObjectExplorerForMySql.Core.ObjectProperties;

/// <summary>Category name from <see cref="PropertyTexts"/>; the argument is the resource key.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LocalizedCategoryAttribute : CategoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="LocalizedCategoryAttribute"/> class.</summary>
    public LocalizedCategoryAttribute(string key)
        : base(key)
    {
        ResourceKey = key;
    }

    /// <summary>Gets the resource key.</summary>
    public string ResourceKey { get; }

    /// <inheritdoc />
    protected override string GetLocalizedString(string value) => PropertyTexts.Get(value);
}

/// <summary>Property name from <see cref="PropertyTexts"/>; the argument is the resource key.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LocalizedDisplayNameAttribute : DisplayNameAttribute
{
    /// <summary>Initializes a new instance of the <see cref="LocalizedDisplayNameAttribute"/> class.</summary>
    public LocalizedDisplayNameAttribute(string key)
        : base(key)
    {
        ResourceKey = key;
    }

    /// <summary>Gets the resource key.</summary>
    public string ResourceKey { get; }

    /// <inheritdoc />
    public override string DisplayName => PropertyTexts.Get(ResourceKey);
}

/// <summary>
/// Base class of the objects shown in the Visual Studio Properties window (E04). All properties are read-only
/// (getter only). The header of the window shows the object name and its localized type, for example
/// "customer_id  Column".
/// </summary>
public abstract class ObjectPropertiesBase : ICustomTypeDescriptor
{
    /// <summary>Gets the resource key of the object type, for example "TypeColumn".</summary>
    protected abstract string TypeKey { get; }

    /// <summary>Gets the localized object type shown in the header of the Properties window.</summary>
    public string GetTypeName() => PropertyTexts.Get(TypeKey);

    /// <summary>Returns the object name shown in the header of the Properties window.</summary>
    public abstract string GetObjectName();

    AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);

    string ICustomTypeDescriptor.GetClassName() => GetTypeName();

    string ICustomTypeDescriptor.GetComponentName() => GetObjectName();

    TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);

    EventDescriptor ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);

    PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);

    object ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() => TypeDescriptor.GetProperties(this, true);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes) => TypeDescriptor.GetProperties(this, attributes, true);

    object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd) => this;
}

/// <summary>Base class for objects on a server: adds the Connection category (US-0401, US-0402).</summary>
public abstract class ServerObjectPropertiesBase : ObjectPropertiesBase
{
    /// <summary>Initializes a new instance of the <see cref="ServerObjectPropertiesBase"/> class.</summary>
    /// <param name="connection">The server connection, or null when unknown.</param>
    protected ServerObjectPropertiesBase(Connections.ConnectionDefinition? connection)
    {
        Connection = connection;
    }

    /// <summary>Gets the name of the connection in the explorer.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropConnectionName")]
    public string ConnectionName => Connection?.Label ?? string.Empty;

    /// <summary>Gets the host name of the server.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropServer")]
    public string Server => Connection?.Host ?? string.Empty;

    /// <summary>Gets the TCP port of the server.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropPort")]
    public int? Port => Connection?.Port;

    /// <summary>Gets the user name of the connection.</summary>
    [LocalizedCategory(PropertyCategories.Connection)]
    [LocalizedDisplayName("PropUser")]
    public string User => Connection?.UserName ?? string.Empty;

    /// <summary>Gets the connection, or null.</summary>
    protected Connections.ConnectionDefinition? Connection { get; }
}

/// <summary>Resource keys of the property categories.</summary>
internal static class PropertyCategories
{
    public const string General = "CategoryGeneral";
    public const string DataType = "CategoryDataType";
    public const string Storage = "CategoryStorage";
    public const string Description = "CategoryDescription";
    public const string Connection = "CategoryConnection";
}

/// <summary>Formats values for the Properties window.</summary>
public static class PropertyFormat
{
    private static readonly string[] _units = ["bytes", "KB", "MB", "GB", "TB"];

    /// <summary>Formats a size: <c>16 KB</c>, <c>1.5 MB</c>; empty for null. Uses 1024 per step.</summary>
    public static string Bytes(long? bytes)
    {
        if (bytes is not { } value)
        {
            return string.Empty;
        }

        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < _units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} bytes", value)
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##} {1}", size, _units[unit]);
    }

    /// <summary>Joins column names: <c>a, b, c</c>.</summary>
    public static string List(System.Collections.Generic.IEnumerable<string> items) => string.Join(", ", items);
}
