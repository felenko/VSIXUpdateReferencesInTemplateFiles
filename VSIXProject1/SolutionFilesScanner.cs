using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VSIXProject1
{
    internal sealed class SolutionFilesScanner
    {
        public static List<string> ScanSolution(string solutionPath)
        {
            var filesToAnalyze = Directory.GetFiles(solutionPath, "*.*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".tt") || s.EndsWith(".ttinclude") || s.EndsWith("packages.config")).ToList();
            return filesToAnalyze;
        }

        public static IEnumerable<string> GetTemplateFiles(List<string> filesToAnalyze)
        {
            var templateFiles =
                filesToAnalyze.Where(s => (s.EndsWith(".tt") || s.EndsWith(".ttinclude")) && !s.Contains("\\packages\\"));
            return templateFiles;
        }

        public static IEnumerable<string> GetPackageConfigFiles(List<string> filesToAnalyze)
        {
            var packageConfigFiles = filesToAnalyze.Where(s => s.EndsWith("packages.config"));
            return packageConfigFiles;
        }
    }
}