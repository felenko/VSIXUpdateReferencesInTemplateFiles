using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace VSIXProject1
{
    internal sealed class PackageConfigParser
    {
        public static string GetKeyFromPath(string path)
        {
            var foldersHierarchy = path.Split('\\');
            return foldersHierarchy.FirstOrDefault(f => f.StartsWith("BDB"));
        }

        public Dictionary<string, Dictionary<string, string>> BuldReferencesListFromPackages(IEnumerable<string> packageFiles)
        {
            var referencedPackages = new Dictionary<string, Dictionary<string, string>>();
            foreach (var file in packageFiles)
            {
                referencedPackages.Add(GetKeyFromPath(file), ParceReferences(file));
            }

            return referencedPackages;
        }

        private Dictionary<string, string> ParceReferences(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            var referencedPackages = new Dictionary<string, string>();
            XmlNodeList packageNodes = doc.DocumentElement.SelectNodes("//package");
            for (int i = 0; i < packageNodes.Count; i++)
            {
                referencedPackages.Add(packageNodes.Item(i).Attributes["id"].Value, packageNodes.Item(i).Attributes["version"].Value);
            }

            return referencedPackages;
        }
    }
}