﻿using EnvDTE; 
using EnvDTE80;
using GitLab.VisualStudio;
using GitLab.VisualStudio.Services;
using GitLab.VisualStudio.Shared;
using GitLab.VisualStudio.UI.ViewModels;
using GitLab.VisualStudio.UI.Views;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace GitLab.VisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#8110", "#8112", PackageVersion.Version, IconResourceID = 8400)]
    [ProvideMenuResource("Menus2.ctmenu", 1)]
    [Guid(PackageGuids.guidOpenOnGitLabPkgString)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    public sealed class OpenOnGitLabPackage : Package
    {
      
 

        [Import]
        private IShellService _shell;
     
        [Import]
        private IViewFactory _viewFactory;

        private static DTE2 _dte;
        internal static DTE2 DTE
        {
            get
            {
                if (_dte == null)
                {
                    _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                }

                return _dte;
            }
        }
        
        protected override void Initialize()
        {
            base.Initialize();
            var assemblyCatalog = new AssemblyCatalog(typeof(OpenOnGitLabPackage).Assembly);
            CompositionContainer container = new CompositionContainer(assemblyCatalog);
            container.ComposeParts(this);
            var mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (mcs != null)
            {
                foreach (var item in new[]
                {
                    PackageCommanddIDs.OpenMaster,
                    PackageCommanddIDs.OpenBranch,
                    PackageCommanddIDs.OpenRevision,
                    PackageCommanddIDs.OpenRevisionFull,
                     PackageCommanddIDs.OpenBlame,
                     PackageCommanddIDs.OpenCommits,
                     PackageCommanddIDs.CreateSnippet
                })
                {
                    var menuCommandID = new CommandID(PackageGuids.guidOpenOnGitLabCmdSet, (int)item);
                    var menuItem = new OleMenuCommand(ExecuteCommand, menuCommandID);
                    menuItem.BeforeQueryStatus += MenuItem_BeforeQueryStatus;
                    mcs.AddCommand(menuItem);
                }
            }
        }

        private void MenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            var command = (OleMenuCommand)sender;
            try
            {
                if (command.CommandID.ID == PackageCommanddIDs.CreateSnippet)
                {
                    command.Text = Strings.OpenOnGitLabPackage_CreateSnippet;
                    var selectionLineRange = GetSelectionLineRange();
                    command.Enabled = selectionLineRange.Item1 < selectionLineRange.Item2;
                }
                else
                {
                    // TODO:is should avoid create GitAnalysis every call?
                    using (var git = new GitAnalysis(GetActiveFilePath()))
                    {
                        if (!git.IsDiscoveredGitRepository)
                        {
                            command.Enabled = false;
                            return;
                        }

                        var type = ToGitLabUrlType(command.CommandID.ID);
                        var targetPath = git.GetGitLabTargetPath(type);
                        if (type == GitLabUrlType.CurrentBranch && targetPath == "master")
                        {
                            command.Visible = false;
                        }
                        else
                        {
                            command.Text = git.GetGitLabTargetDescription(type);
                            command.Enabled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var exstr = ex.ToString();
                Debug.Write(exstr);
                command.Text = "error:" + ex.GetType().Name;
                command.Enabled = false;
            }
        }

        private void ExecuteCommand(object sender, EventArgs e)
        {
            var command = (OleMenuCommand)sender;
            try
            {
                if (command.CommandID.ID == PackageCommanddIDs.CreateSnippet)
                {
                    var selection = DTE.ActiveDocument.Selection as TextSelection;
                    if (selection != null)
                    {
                        var dialog = _viewFactory.GetView<Dialog>(ViewTypes.CreateSnippet);
                        var cs = (CreateSnippet)dialog;
                        var csm = cs.DataContext as CreateSnippetViewModel;
                        csm.Code = selection.Text;
                        csm.FileName = new System.IO.FileInfo(DTE.ActiveDocument.FullName).Name; 
                        _shell.ShowDialog(Strings.OpenOnGitLabPackage_CreateSnippet, dialog);
                    }
                    else
                    {
                        Debug.Write("未选择任何内容");
                    }
                }
                else
                {
                    using (var git = new GitAnalysis(GetActiveFilePath()))
                    {
                        if (!git.IsDiscoveredGitRepository)
                        {
                            return;
                        }
                        var selectionLineRange = GetSelectionLineRange();
                        var type = ToGitLabUrlType(command.CommandID.ID);
                        var GitLabUrl = git.BuildGitLabUrl(type, selectionLineRange);
                        System.Diagnostics.Process.Start(GitLabUrl); // open browser
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Write(ex.ToString());
            }
        }

        string GetActiveFilePath()
        {
            // sometimes, DTE.ActiveDocument.Path is ToLower but GitLab can't open lower path.
            // fix proper-casing | http://stackoverflow.com/questions/325931/getting-actual-file-name-with-proper-casing-on-windows-with-net
            var path = GetExactPathName(DTE.ActiveDocument.Path + DTE.ActiveDocument.Name);
            return path;
        }

        static string GetExactPathName(string pathName)
        {
            if (!(File.Exists(pathName) || Directory.Exists(pathName)))
                return pathName;

            var di = new DirectoryInfo(pathName);

            if (di.Parent != null)
            {
                return Path.Combine(
                    GetExactPathName(di.Parent.FullName),
                    di.Parent.GetFileSystemInfos(di.Name)[0].Name);
            }
            else
            {
                return di.Name.ToUpper();
            }
        }

        Tuple<int, int> GetSelectionLineRange()
        {
            var selection = DTE.ActiveDocument.Selection as TextSelection;
            if (selection != null)
            {
                if (!selection.IsEmpty)
                {
                    return Tuple.Create(selection.TopPoint.Line, selection.BottomPoint.Line);
                }
                else
                {
                    return Tuple.Create(selection.CurrentLine, selection.CurrentLine);
                }
            }
            else
            {
                return null;
            }
        }
        static GitLabUrlType ToGitLabUrlType(int commandId)
        {
            if (commandId == PackageCommanddIDs.OpenMaster) return GitLabUrlType.Master;
            if (commandId == PackageCommanddIDs.OpenBranch) return GitLabUrlType.CurrentBranch;
            if (commandId == PackageCommanddIDs.OpenRevision) return GitLabUrlType.CurrentRevision;
            if (commandId == PackageCommanddIDs.OpenRevisionFull) return GitLabUrlType.CurrentRevisionFull;
            if (commandId == PackageCommanddIDs.OpenBlame) return GitLabUrlType.Blame;
            if (commandId == PackageCommanddIDs.OpenCommits) return GitLabUrlType.Commits;
            else return GitLabUrlType.Master;
        }
    }
}
