using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ProWeighSuitePlatforms.Tests.Architecture
{
    public sealed class ProjectBoundaryTests
    {
        private static readonly string Root = FindRepositoryRoot();

        private const string CoreProject = "src/ProWeighSuitePlatforms.Core/ProWeighSuitePlatforms.Core.csproj";
        private const string FusionModuleProject = "src/ProWeighSuitePlatforms.Modules.Fusion/ProWeighSuitePlatforms.Modules.Fusion.csproj";
        private const string FusionAppProject = "src/ProWeighSuitePlatforms.Fusion/ProWeighSuitePlatforms.Fusion.csproj";

        [Fact]
        public void CoreLibraryHasNoProjectReferences()
        {
            Assert.Empty(ProjectReferences(CoreProject));
        }

        [Fact]
        public void FusionModuleReferencesOnlyCore()
        {
            var references = ProjectReferences(FusionModuleProject);

            Assert.Single(references);
            Assert.Equal(Normalize(Path.GetFullPath(Path.Combine(Root, CoreProject))), references[0]);
        }

        [Fact]
        public void FusionAppReferencesCoreAndFusionModuleOnly()
        {
            var references = ProjectReferences(FusionAppProject);

            Assert.Equal(2, references.Length);
            Assert.Contains(Normalize(Path.GetFullPath(Path.Combine(Root, CoreProject))), references);
            Assert.Contains(Normalize(Path.GetFullPath(Path.Combine(Root, FusionModuleProject))), references);
        }

        [Fact]
        public void FusionAppOutputContainsOnlyTheFusionModule()
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
            var output = Path.Combine(Root, "src", "ProWeighSuitePlatforms.Fusion", "bin", configuration, "net8.0-windows");

            Assert.True(Directory.Exists(output), $"Missing app output: {output}");

            var modules = Directory.GetFiles(output, "ProWeighSuitePlatforms.Modules.*.dll")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var expected = new[] { "ProWeighSuitePlatforms.Modules.Fusion.dll" };
            Assert.Equal(expected, modules);

            // The consolidated core assembly must be present as well.
            Assert.True(
                File.Exists(Path.Combine(output, "ProWeighSuitePlatforms.Core.dll")),
                "Missing consolidated core assembly in app output.");
        }

        private static string[] ProjectReferences(string projectRelativePath)
        {
            var full = Path.Combine(Root, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"Missing project: {full}");

            var projectDir = Path.GetDirectoryName(full)!;
            var document = XDocument.Load(full);
            var root = document.Root!;
            XNamespace ns = root.GetDefaultNamespace();

            return root
                .Descendants(ns + "ProjectReference")
                .Attributes("Include")
                .Select(a => Normalize(Path.GetFullPath(Path.Combine(projectDir, a.Value))))
                .ToArray();
        }

        private static string Normalize(string value) =>
            value.Replace('\\', '/');

        private static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
}
