using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using ParvNegar.Application;
using ParvNegar.Domain;

namespace ParvNegar.App
{
    /// <summary>
    /// Reads the school profile from Config\school-profile.xml. Text is read with whitespace preserved,
    /// so the title reaches the UI exactly as written. Invalid or missing configuration returns null
    /// (the shell then shows a neutral message instead of crashing).
    /// </summary>
    public sealed class XmlSchoolProfileSource : ISchoolProfileSource
    {
        private readonly string _path;

        public XmlSchoolProfileSource(string path) { _path = path; }

        public static XmlSchoolProfileSource Default() =>
            new XmlSchoolProfileSource(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "school-profile.xml"));

        public SchoolProfile Load()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 };
                using (var reader = XmlReader.Create(_path, settings))
                {
                    var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                    var root = doc.Root;
                    if (root == null || root.Name.LocalName != "SchoolProfile") return null;
                    var id = Guid.Parse((string)root.Attribute("Id"));
                    var name = (string)root.Element("SchoolName");
                    var title = (string)root.Element("HomeTitle");
                    var logo = (string)root.Element("LogoAssetId");
                    return new SchoolProfile(id, name, title, logo);
                }
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is ArgumentException || ex is DomainException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
