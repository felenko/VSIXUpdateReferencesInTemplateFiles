using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VSIXUpdateTemplates
{
    internal sealed class TemplateFilesProcessor
    {
        public int filesUpdated;
        public int referencesUpdated;

        public void ProcessTemplateFiles(IEnumerable<string> templateFiles, Dictionary<string, Dictionary<string, string>> referencedPackages)
        {
            foreach (var templateFilePath in templateFiles)
            {
                this.ProcessOneTemplateFile(templateFilePath, referencedPackages);
            }
        }

        private void ProcessOneTemplateFile(string templateFilePath, Dictionary<string, Dictionary<string, string>> referencedPackages)
        {
            IEnumerable<string> text = File.ReadLines(templateFilePath);
            var references = FindReferencesForTemplateFile(templateFilePath, referencedPackages);
            bool fileUpdated = false;
            var newFileLines = new StringBuilder();
            foreach (string line in text)
            {
                bool lineNotChanged = true;
                if (line.TrimStart(' ').StartsWith(@"<#@ assembly Name=""$(SolutionDir)"))
                {
                    (string package, string version) = ExtractPackageAndVersion(line);
                    string referencedVersion = string.Empty;
                    if (!string.IsNullOrEmpty(package) && !string.IsNullOrEmpty(version))
                    {
                        references.TryGetValue(package.TrimEnd('.'), out referencedVersion);
                        if (string.IsNullOrEmpty(referencedVersion))
                        {
                            // Warning package is not referenced but assembly is referenced
                        }

                        if (referencedVersion != version && !string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(referencedVersion))
                        {
                            this.referencesUpdated++;
                            newFileLines.AppendLine(line.Replace(version, referencedVersion));
                            fileUpdated = true; lineNotChanged = false;
                        }
                    }
                }
                if (lineNotChanged) newFileLines.AppendLine(line);
            }

            if (fileUpdated)
            {
                File.WriteAllText(templateFilePath, newFileLines.ToString());
                this.filesUpdated++;
            }
        }

        private (string package, string version) ExtractPackageAndVersion(string line)
        {
            var regex = new Regex(@"\\([a-zA-Z0-9.]*)(\d+.\d+.\d+.\d+)\\");
            Match match = regex.Match(line);
            if (match.Success) return (match.Groups[1].Value, match.Groups[2].Value);
            return (null, null);
        }

        private Dictionary<string, string> FindReferencesForTemplateFile(string templateFilePath, Dictionary<string, Dictionary<string, string>> referencedPackages)
        {
            var key = PackageConfigParser.GetKeyFromPath(templateFilePath);
            referencedPackages.TryGetValue(key, out var referencesForSpecificProject);
            return referencesForSpecificProject;
        }
    }
}