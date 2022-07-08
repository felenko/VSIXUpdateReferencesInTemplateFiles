using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace VSIXUpdateTemplates
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

        private TemplateFilesProcessor _templateFilesProcessor;

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

        private const string title = "Update Templates";

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            this.StatusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
            DTE dte = Package.GetGlobalService(typeof(SDTE)) as DTE;

            Stopwatch processStopwatch = new Stopwatch();
            processStopwatch.Start();
            var solutionPath = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            StatusBar.SetText("Scanning Solution...");
            var filesToAnalyze = SolutionFilesScanner.ScanSolution(solutionPath);
            var packageConfigFiles = SolutionFilesScanner.GetPackageConfigFiles(filesToAnalyze);
            var templateFiles = SolutionFilesScanner.GetTemplateFiles(filesToAnalyze);
            StatusBar.SetText("Parsing packages.config files...");
            var referencedPackages = new PackageConfigParser().BuldReferencesListFromPackages(packageConfigFiles);

            StatusBar.SetText("Processing template files...");
            this._templateFilesProcessor = new TemplateFilesProcessor();
            this._templateFilesProcessor.ProcessTemplateFiles(templateFiles, referencedPackages);

            VsShellUtilities.ShowMessageBox(
                this.package,
                string.Format("Update completed. {0} references updated in {1} files. Processed in {2} ms", this._templateFilesProcessor.referencesUpdated, this._templateFilesProcessor.filesUpdated, processStopwatch.ElapsedMilliseconds),
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            processStopwatch.Stop();
            StatusBar.Clear();
        }

        public IVsStatusbar StatusBar { get; set; }
    }
}