using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Serialization;
using NuGet.Packaging.Core;

namespace SbomForge.Resolver;

[XmlRoot("package", Namespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")]
public class Nuspec
{
    [XmlElement("metadata")]
    public Metadata Metadata { get; set; }

    [XmlAttribute("minClientVersion")]
    public string MinClientVersion { get; set; }

    public static Nuspec? FromFile(Stream stream)
    {
        var serializer = new XmlSerializer(typeof(Nuspec));
        return (Nuspec?)serializer.Deserialize(stream);
    }
}

public class Metadata
{
    // Required elements
    [XmlElement("id")]
    public string Id { get; set; }

    [XmlElement("version")]
    public string Version { get; set; }

    [XmlElement("authors")]
    public string Authors { get; set; }

    [XmlElement("description")]
    public string Description { get; set; }

    // Optional elements
    [XmlElement("owners")]
    public string Owners { get; set; }

    [XmlElement("projectUrl")]
    public string ProjectUrl { get; set; }

    [XmlElement("iconUrl")]
    public string IconUrl { get; set; }

    [XmlElement("licenseUrl")]
    public string LicenseUrl { get; set; } // Deprecated, prefer 'license' element

    [XmlElement("license")]
    public License License { get; set; }

    [XmlElement("requireLicenseAcceptance")]
    public bool? RequireLicenseAcceptance { get; set; }

    [XmlElement("developmentDependency")]
    public bool? DevelopmentDependency { get; set; }

    [XmlElement("tags")]
    public string Tags { get; set; }

    [XmlElement("releaseNotes")]
    public string ReleaseNotes { get; set; }

    [XmlElement("copyright")]
    public string Copyright { get; set; }

    [XmlElement("language")]
    public string Language { get; set; }

    [XmlElement("serviceable")]
    public bool? Serviceable { get; set; }

    [XmlElement("repository")]
    public Repository Repository { get; set; }

    [XmlElement("summary")]
    public string Summary { get; set; }

    [XmlElement("title")]
    public string Title { get; set; }
}

public class License
{
    [XmlAttribute("type")]
    public string Type { get; set; } // Possible values: 'expression', 'file'

    [XmlText]
    public string Text { get; set; }
}

public class Repository
{
    [XmlAttribute("type")]
    public string Type { get; set; } // e.g., git, tfs, svn

    [XmlAttribute("url")]
    public string Url { get; set; }

    [XmlAttribute("branch")]
    public string Branch { get; set; }

    [XmlAttribute("commit")]
    public string Commit { get; set; }
}


