using System.ComponentModel;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace SbomForge.Resolver;

#pragma warning disable CS1591

[EditorBrowsable(EditorBrowsableState.Never)]
[XmlRoot("package")]
public class Nuspec
{
    private static readonly XmlSerializer _serializer = new(typeof(Nuspec));

    [XmlElement("metadata")]
    public Metadata? Metadata { get; set; }

    [XmlAttribute("minClientVersion")]
    public string? MinClientVersion { get; set; }

    public static Nuspec? FromFile(Stream stream)
    {
        XDocument normalized = RemoveNamespaces(XDocument.Load(stream));
        using XmlReader reader = normalized.CreateReader();
        return (Nuspec?)_serializer.Deserialize(reader);
    }

    private static XDocument RemoveNamespaces(XDocument document)
    {
        return new XDocument(document.Declaration, document.Root is null ? null : RemoveNamespaces(document.Root));
    }

    private static XElement RemoveNamespaces(XElement element)
    {
        object[] content =
        [
            .. element.Attributes()
                .Where(static attribute => !attribute.IsNamespaceDeclaration)
                .Select(static attribute => new XAttribute(attribute.Name.LocalName, attribute.Value)),
            .. element.Nodes().Select(RemoveNamespaces)
        ];

        return new XElement(element.Name.LocalName, content);
    }

    private static XNode RemoveNamespaces(XNode node)
    {
        return node switch
        {
            XElement element => RemoveNamespaces(element),
            XCData cdata => new XCData(cdata.Value),
            XText text => new XText(text.Value),
            XComment comment => new XComment(comment.Value),
            XProcessingInstruction instruction => new XProcessingInstruction(instruction.Target, instruction.Data),
            _ => node
        };
    }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public class Metadata
{
    // Required elements
    [XmlElement("id")]
    public string? Id { get; set; }

    [XmlElement("version")]
    public string? Version { get; set; }

    [XmlElement("authors")]
    public string? Authors { get; set; }

    [XmlElement("description")]
    public string? Description { get; set; }

    // Optional elements
    [XmlElement("owners")]
    public string? Owners { get; set; }

    [XmlElement("projectUrl")]
    public string? ProjectUrl { get; set; }

    [XmlElement("iconUrl")]
    public string? IconUrl { get; set; }

    [XmlElement("licenseUrl")]
    public string? LicenseUrl { get; set; } // Deprecated, prefer 'license' element

    [XmlElement("license")]
    public License? License { get; set; }

    [XmlElement("requireLicenseAcceptance")]
    public bool? RequireLicenseAcceptance { get; set; }

    [XmlElement("developmentDependency")]
    public bool? DevelopmentDependency { get; set; }

    [XmlElement("tags")]
    public string? Tags { get; set; }

    [XmlElement("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    [XmlElement("copyright")]
    public string? Copyright { get; set; }

    [XmlElement("language")]
    public string? Language { get; set; }

    [XmlElement("serviceable")]
    public bool? Serviceable { get; set; }

    [XmlElement("repository")]
    public Repository? Repository { get; set; }

    [XmlElement("summary")]
    public string? Summary { get; set; }

    [XmlElement("title")]
    public string? Title { get; set; }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public class License
{
    [XmlAttribute("type")]
    public string? Type { get; set; } // Possible values: 'expression', 'file'

    [XmlText]
    public string? Text { get; set; }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public class Repository
{
    [XmlAttribute("type")]
    public string? Type { get; set; } // e.g., git, tfs, svn

    [XmlAttribute("url")]
    public string? Url { get; set; }

    [XmlAttribute("branch")]
    public string? Branch { get; set; }

    [XmlAttribute("commit")]
    public string? Commit { get; set; }
}

#pragma warning restore CS1591


