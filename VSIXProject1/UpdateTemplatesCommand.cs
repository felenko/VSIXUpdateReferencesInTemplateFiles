using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using EnvDTE;
using EnvDTE100;
using Microsoft.VisualStudio.TaskStatusCenter;
using MSXML;
using Task = System.Threading.Tasks.Task;

namespace VSIXProject1
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class UpdateTemplatesCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("a3db0c67-5945-4a9d-98c8-8789ac24ae8f");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        private int filesUpdated;
        private int referencesUpdated;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateTemplatesCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private UpdateTemplatesCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static UpdateTemplatesCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in UpdateTemplatesCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new UpdateTemplatesCommand(package, commandService);
        }

        private const string title = "UpdateTemplatesCommand";

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            this.filesUpdated = 0;
            this.referencesUpdated = 0;
            ThreadHelper.ThrowIfNotOnUIThread();
            // StartAsync();
            this.StatusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);

            // Show a message box to prove we were here

            DTE dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
            var statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar3;
            //var name = projects.Item(1).Name;
            //ProjectItems items = projects.Item(1).ProjectItems;
            //foreach (ProjectItem item in items)
            //{
            //    var itemName = item.Name;
            //}
            Stopwatch processStopwatch = new Stopwatch();
            processStopwatch.Start();
            var solutionPath = System.IO.Path.GetDirectoryName(dte.Solution.FullName);

            StatusBar.SetText("Scanning Solution...");
            var filesToAnalyze = Directory.GetFiles(solutionPath, "*.*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".tt") || s.EndsWith(".ttinclude") || s.EndsWith("packages.config")).ToList();
            var fullLinqsearch = processStopwatch.ElapsedMilliseconds;
            var packageConfigFiles = filesToAnalyze.Where(s => s.EndsWith("packages.config"));
            var templateFiles = filesToAnalyze.Where(s => (s.EndsWith(".tt") || s.EndsWith(".ttinclude")) && !s.Contains("\\packages\\"));
            StatusBar.SetText("Parsing packages.config files...");
            var referencedPackages = this.BuldReferencesListFromPachages(packageConfigFiles);

            //var templateFiles = Directory.GetFiles(solutionPath, "*.tt", SearchOption.AllDirectories);
            StatusBar.SetText("Processing template files...");
            processTemplateFiles(templateFiles, referencedPackages);

            VsShellUtilities.ShowMessageBox(
                this.package,
                string.Format("Update completed. {0} references updated in {1} files. Processed in {2} ms", this.referencesUpdated, this.filesUpdated, processStopwatch.ElapsedMilliseconds),
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            processStopwatch.Stop();
            StatusBar.Clear();
        }

        public IVsStatusbar StatusBar { get; set; }

        private void processTemplateFiles(IEnumerable<string> templateFiles, Dictionary<string, Dictionary<string, string>> referencedPackages)
        {
            foreach (var templateFilePath in templateFiles)
            {
                this.ProcessOneTemplate(templateFilePath, referencedPackages);
            }
        }

        private void ProcessOneTemplate(string templateFilePath, Dictionary<string, Dictionary<string, string>> referencedPackages)
        {
            IEnumerable<string> text = File.ReadLines(templateFilePath);
            var references = FindReferencesForTemplateFile(templateFilePath, referencedPackages);
            bool updated = false;
            var newFileLines = new StringBuilder();
            foreach (string line in text)
            {
                if (line.TrimStart(' ').StartsWith(@"<#@ assembly Name=""$(SolutionDir)"))
                {
                    (string package, string version) = extractPackageAndVersion(line);
                    string referencedVersion = string.Empty;
                    if (!string.IsNullOrEmpty(package) && !string.IsNullOrEmpty(version))
                    {
                        references.TryGetValue(package.TrimEnd('.'), out referencedVersion);
                        if (string.IsNullOrEmpty(referencedVersion))
                        {
                            //VsShellUtilities.ShowMessageBox(
                            //    this.package,
                            //    string.Format(
                            //        "Package referenced in Template,but not in packages.config {0}, package: {1}, referenced in config: {2}, current {3}",
                            //        templateFilePath, package, referencedVersion, version),
                            //    title,
                            //    OLEMSGICON.OLEMSGICON_WARNING,
                            //    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            //    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        }

                        if (referencedVersion != version && !string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(referencedVersion))
                        {
                            referencesUpdated++;
                            newFileLines.AppendLine(line.Replace(version, referencedVersion));
                            updated = true;
                        }
                        else
                        {
                            newFileLines.AppendLine(line);
                        }
                    }
                    else
                    {
                        newFileLines.AppendLine(line);
                    }
                }
                else newFileLines.AppendLine(line);
            }

            if (updated)
            {
                File.WriteAllText(templateFilePath, newFileLines.ToString());
                filesUpdated++;
            }
        }

        private (string package, string version) extractPackageAndVersion(string line)
        {
            var regex = new Regex(@"\\([a-zA-Z.]*)(\d+.\d+.\d+.\d+)\\");
            Match match = regex.Match(line);
            if (match.Success) return (match.Groups[1].Value, match.Groups[2].Value);
            return (null, null);
        }

        private Dictionary<string, string> FindReferencesForTemplateFile(string templateFilePath, Dictionary<string, Dictionary<string, string>> referencedPackages)
        {
            var key = GetKeyFromPath(templateFilePath);
            referencedPackages.TryGetValue(key, out var referencesForSpecificProject);
            return referencesForSpecificProject;
        }

        private string GetKeyFromPath(string path)
        {
            var foldersHierarchy = path.Split('\\');
            return foldersHierarchy.FirstOrDefault(f => f.StartsWith("BDB"));
        }

        private Dictionary<string, Dictionary<string, string>> BuldReferencesListFromPachages(IEnumerable<string> packageFiles)
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

        private async Task StartAsync()
        {
            var messages = new string[] { "message1 ", "message2 ", "message3 ", "message4 ", "message5 " };

            var StatusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            uint cookie = 0;
            // Initialize the progress bar.
            StatusBar.Progress(ref cookie, 1, "", 0, 0);

            for (int j = 0; j < 5; j++)
            {
                //Do long running task here
                int count = j + 1;
                StatusBar.Progress(ref cookie, 1, "", (uint)j * 100, 5 * 100);
                StatusBar.SetText(messages[j]);
            }

            // Clear the progress bar.
            StatusBar.Progress(ref cookie, 0, "", 0, 0);
            StatusBar.FreezeOutput(0);
            StatusBar.Clear();

            var tsc = await ServiceProvider.GetServiceAsync(typeof(SVsTaskStatusCenterService)) as IVsTaskStatusCenterService;

            var options = default(TaskHandlerOptions);
            options.Title = "My long running task";
            options.ActionsAfterCompletion = CompletionActions.None;

            TaskProgressData data = default;
            data.CanBeCanceled = true;

            ITaskHandler handler = tsc.PreRegister(options, data);
            Task task = LongRunningTaskAsync(data, handler);
            handler.RegisterTask(task);
        }

        private async Task LongRunningTaskAsync(TaskProgressData data, ITaskHandler handler)
        {
            float totalSteps = 100;

            for (float currentStep = 1; currentStep <= totalSteps; currentStep++)
            {
                await Task.Delay(1000);

                data.PercentComplete = (int)(currentStep / totalSteps * 100);
                data.ProgressText = $"Step {currentStep} of {totalSteps} completed";
                handler.Progress.Report(data);
            }
        }
    }
}