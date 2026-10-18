using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Siemens.Engineering;
using Siemens.Engineering.Hmi.Faceplate;
using Siemens.Engineering.Hmi.Globalization;
using Siemens.Engineering.Hmi.Screen;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Exports and imports project pictures and the project-library faceplate and style types
    /// Openness can round-trip. Global libraries are left out.
    /// </summary>
    public static class ProjectContentExchange
    {
        public static int Export(Project project, string repositoryPath, CancellationToken ct, Action<string> report)
        {
            if (project == null)
            {
                return 0;
            }

            string projectFolder = ProjectContentStore.ProjectFolder(repositoryPath, project.Name);
            IReadOnlyDictionary<string, string> previous = HmiExportNormalizer.CaptureCreationTimestamps(projectFolder);
            int count = 0;
            count += ExportPictures(project, projectFolder, ct, report);
            count += ExportLibrary(project, projectFolder, ct, report);
            HmiExportNormalizer.StabilizeCreationTimestamps(projectFolder, previous);
            return count;
        }

        public static int Import(Project project, string repositoryPath, CancellationToken ct)
        {
            if (project == null)
            {
                return 0;
            }

            string projectFolder = ResolveProjectFolder(repositoryPath, project.Name);
            if (string.IsNullOrEmpty(projectFolder))
            {
                return 0;
            }

            int count = 0;
            count += ImportPictures(project, projectFolder, ct);
            count += ImportLibrary(project, projectFolder, ct);
            return count;
        }

        private static int ExportPictures(Project project, string projectFolder, CancellationToken ct, Action<string> report)
        {
            report?.Invoke(HmiExportProgress.Format(project.Name, "Pictures", "running", 0, string.Empty));
            GraphicsProvider provider;
            try
            {
                provider = project.GetService<GraphicsProvider>();
            }
            catch (Exception ex)
            {
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Project pictures for \"" + project.Name + "\" could not be read.", ex);
                }

                HmiExportTrace.Error(ex);
                report?.Invoke(HmiExportProgress.Format(project.Name, "Pictures", "skipped", 0, ex.Message));
                return 0;
            }

            if (provider?.Graphics == null)
            {
                report?.Invoke(HmiExportProgress.Format(project.Name, "Pictures", "skipped", 0, "This project has no graphic collection."));
                return 0;
            }

            string folder = ProjectContentStore.GraphicsFolder(projectFolder);
            ReplaceDirectory(folder);
            var names = new List<string>();
            int count = 0;
            foreach (MultiLingualGraphic graphic in provider.Graphics)
            {
                ct.ThrowIfCancellationRequested();
                if (graphic == null)
                {
                    continue;
                }

                string name = graphic.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                try
                {
                    graphic.Export(
                        new FileInfo(Path.Combine(folder, HmiExportStore.FolderName(name) + ".xml")),
                        ExportOptions.WithDefaults);
                    names.Add(name);
                    count++;
                }
                catch (Exception ex)
                {
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException("Project picture \"" + name + "\" could not be exported.", ex);
                    }

                    HmiExportTrace.Error(ex);
                }
            }

            ProjectContentStore.WriteGraphicIndex(folder, names);
            report?.Invoke(HmiExportProgress.Format(project.Name, "Pictures", "done", count, string.Empty));
            return count;
        }

        private static int ImportPictures(Project project, string projectFolder, CancellationToken ct)
        {
            string folder = ProjectContentStore.GraphicsFolder(projectFolder);
            IReadOnlyList<string> names = ProjectContentStore.ReadGraphicIndex(folder);
            if (!Directory.Exists(folder) || names == null)
            {
                return 0;
            }

            GraphicsProvider provider = project.GetService<GraphicsProvider>();
            MultiLingualGraphicComposition graphics = provider == null ? null : provider.Graphics;
            if (graphics == null)
            {
                return 0;
            }

            int count = 0;
            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                ct.ThrowIfCancellationRequested();
                graphics.Import(new FileInfo(file), ImportOptions.Override);
                count++;
            }

            var keep = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var existing = new List<MultiLingualGraphic>();
            foreach (MultiLingualGraphic graphic in graphics)
            {
                existing.Add(graphic);
            }

            foreach (MultiLingualGraphic graphic in existing)
            {
                string name = graphic == null ? string.Empty : graphic.Name ?? string.Empty;
                if (name.Length > 0 && !keep.Contains(name))
                {
                    graphic.Delete();
                }
            }

            return count;
        }

        private static int ExportLibrary(Project project, string projectFolder, CancellationToken ct, Action<string> report)
        {
            report?.Invoke(HmiExportProgress.Format(project.Name, "Library", "running", 0, string.Empty));
            ProjectLibrary library = project.ProjectLibrary;
            if (library?.TypeFolder == null)
            {
                report?.Invoke(HmiExportProgress.Format(project.Name, "Library", "skipped", 0, "This project has no library."));
                return 0;
            }

            string folder = ProjectContentStore.LibraryFolder(projectFolder);
            ReplaceDirectory(folder);
            int count = ExportFolder(library.TypeFolder, folder, ct);
            report?.Invoke(HmiExportProgress.Format(project.Name, "Library", "done", count, string.Empty));
            return count;
        }

        private static int ExportFolder(LibraryTypeFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            int count = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LibraryType type in folder.Types)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSharedType(type))
                {
                    continue;
                }

                count += ExportType(type, directory, used);
            }

            foreach (LibraryTypeUserFolder child in folder.Folders)
            {
                ct.ThrowIfCancellationRequested();
                string name = child == null ? string.Empty : child.Name ?? string.Empty;
                if (name.Length == 0)
                {
                    continue;
                }

                count += ExportFolder(child, Path.Combine(directory, HmiExportStore.UniqueName(name, used)), ct);
            }

            return count;
        }

        private static int ExportType(LibraryType type, string directory, ISet<string> used)
        {
            LibraryTypeVersion version = ChooseVersion(type);
            if (version == null)
            {
                return 0;
            }

            string format = string.Empty;
            try
            {
                format = ProjectContentStore.ChooseFormat(type.GetSupportedExportFormats());
            }
            catch (Exception ex)
            {
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Library type \"" + type.Name + "\" could not be read.", ex);
                }

                HmiExportTrace.Error(ex);
            }

            string typeFolder = Path.Combine(directory, HmiExportStore.UniqueName(type.Name, used));
            Directory.CreateDirectory(typeFolder);
            string fileBase = HmiExportStore.FolderName(type.Name);
            try
            {
                if (format.Length > 0)
                {
                    version.ExportAsDocuments(
                        new DirectoryInfo(typeFolder),
                        fileBase,
                        format,
                        LibraryExportOptions.WithLibraryVersionInfoFile);
                }
                else
                {
                    format = "Export";
                    version.Export(new FileInfo(Path.Combine(typeFolder, fileBase + ".xml")), ExportOptions.WithDefaults);
                }
            }
            catch (Exception ex)
            {
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Library type \"" + type.Name + "\" could not be exported.", ex);
                }

                HmiExportTrace.Error(ex);
                return 0;
            }

            ProjectContentStore.WriteManifest(typeFolder, type.Name, type.Guid, format, fileBase);
            return 1;
        }

        private static int ImportLibrary(Project project, string projectFolder, CancellationToken ct)
        {
            string folder = ProjectContentStore.LibraryFolder(projectFolder);
            ProjectLibrary library = project.ProjectLibrary;
            if (!Directory.Exists(folder) || library?.TypeFolder == null)
            {
                return 0;
            }

            return ImportFolder(library, library.TypeFolder, folder, ct);
        }

        private static int ImportFolder(ProjectLibrary library, LibraryTypeFolder parent, string directory, CancellationToken ct)
        {
            int count = 0;
            foreach (string child in Directory.GetDirectories(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(Path.Combine(child, ProjectContentStore.ManifestName)))
                {
                    if (ImportType(library, parent, child))
                    {
                        count++;
                    }

                    continue;
                }

                string name = Path.GetFileName(child);
                LibraryTypeUserFolder folder = FindFolder(parent, name) ?? parent.Folders.Create(name);
                count += ImportFolder(library, folder, child, ct);
            }

            return count;
        }

        private static bool ImportType(ProjectLibrary library, LibraryTypeFolder parent, string typeFolder)
        {
            if (!ProjectContentStore.TryReadManifest(typeFolder, out string name, out Guid guid, out string format, out string fileBase))
            {
                return false;
            }

            try
            {
                LibraryType existing = library.FindType(guid);
                if (existing != null)
                {
                    existing.Versions.CreateFromDocuments(
                        new DirectoryInfo(typeFolder),
                        fileBase,
                        CreateOptions.Override,
                        LibraryImportOptions.None);
                    return true;
                }

                parent.Types.CreateFromDocuments(
                    new DirectoryInfo(typeFolder),
                    fileBase,
                    LibraryImportOptions.None);
                return true;
            }
            catch (Exception ex)
            {
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Library type \"" + name + "\" could not be imported.", ex);
                }

                HmiExportTrace.Error(ex);
                return false;
            }
        }

        private static LibraryTypeUserFolder FindFolder(LibraryTypeFolder parent, string name)
        {
            foreach (LibraryTypeUserFolder folder in parent.Folders)
            {
                if (folder != null && string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }

            return null;
        }

        private static LibraryTypeVersion ChooseVersion(LibraryType type)
        {
            LibraryTypeVersion fallback = null;
            foreach (LibraryTypeVersion version in type.Versions)
            {
                if (version == null)
                {
                    continue;
                }

                if (version.State == LibraryTypeVersionState.InWork)
                {
                    return version;
                }

                if (version.IsDefault || fallback == null)
                {
                    fallback = version;
                }
            }

            return fallback;
        }

        private static bool IsSharedType(LibraryType type)
        {
            return type is FaceplateLibraryType || type is StyleLibraryType || type is StyleSheetLibraryType;
        }

        private static string ResolveProjectFolder(string repositoryPath, string projectName)
        {
            string expected = ProjectContentStore.ProjectFolder(repositoryPath, projectName);
            if (Directory.Exists(expected))
            {
                return expected;
            }

            string root = ProjectContentStore.SharedRoot(repositoryPath);
            if (!Directory.Exists(root))
            {
                return string.Empty;
            }

            string[] folders = Directory.GetDirectories(root);
            return folders.Length == 1 ? folders[0] : string.Empty;
        }

        private static void ReplaceDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            Directory.CreateDirectory(directory);
        }

        private static bool IsSessionDead(Exception ex)
        {
            return ex is NonRecoverableException || ex is EngineeringObjectDisposedException;
        }
    }
}
