// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;

namespace NuGet.Test.Utility
{
    public static class TestFileSystemUtility
    {
        private static readonly Lazy<string> RootDirectoryLazy = new Lazy<string>(() => GetRootDirectory());
        private static readonly Lazy<bool> SkipCleanupLazy = new Lazy<bool>(() => SkipCleanUp());
        private const string DotnetCliBinary = "dotnet";
        private const string DotnetCliExe = "dotnet.exe";

        /// <summary>
        /// Root test folder where temporary test outputs should go.
        /// </summary>
        public static string NuGetTestFolder => RootDirectoryLazy.Value;

        private static bool SkipCleanUp()
        {
            // Option to leave files around for debugging
            var val = Environment.GetEnvironmentVariable("NUGET_PERSIST_TESTFOLDERS");

            if (StringComparer.OrdinalIgnoreCase.Equals(val, "true"))
            {
                return true;
            }

            return false;
        }

        private static string GetRootDirectory()
        {
            string repoRoot = GetRepositoryRoot();

            DirectoryInfo root;

            if (repoRoot == null)
            {
                // Use a folder under %TEMP% if the repository root can't be determined
                root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NuGetTestFolder"));
            }
            else
            {
                DirectoryInfo testDirectory = Directory.CreateDirectory(Path.Combine(repoRoot, ".test"));

                try
                {
                    File.WriteAllText(Path.Combine(testDirectory.FullName, "Directory.Build.props"), "<Project />");
                    File.WriteAllText(Path.Combine(testDirectory.FullName, "Directory.Build.targets"), "<Project />");
                    File.WriteAllText(Path.Combine(testDirectory.FullName, "Directory.Solution.props"), "<Project />");
                    File.WriteAllText(Path.Combine(testDirectory.FullName, "Directory.Solution.targets"), "<Project />");
                    File.WriteAllText(Path.Combine(testDirectory.FullName, "Directory.Build.rsp"), string.Empty);
                    File.WriteAllText(Path.Combine(testDirectory.FullName, "Directory.Packages.props"), "<Project />");
                }
                catch (Exception)
                {
                    // Ignored
                }

                root = testDirectory.CreateSubdirectory("work");
            }

            return root.FullName;
        }

        private static string GetRepositoryRoot()
        {
            // First, check if an override is specified
            string testWorkPathOverride = Environment.GetEnvironmentVariable("NUGET_TEST_WORK_PATH");
            if (!string.IsNullOrEmpty(testWorkPathOverride))
            {
                // Override if set
                return new DirectoryInfo(testWorkPathOverride).FullName;
            }

            // Second, check next to this assembly
            var assemblyFileInfo = new FileInfo(typeof(TestFileSystemUtility).Assembly.Location);

            DirectoryInfo repoRoot = GetRepositoryRootFromStartingDirectory(assemblyFileInfo.Directory);

            if (repoRoot != null)
            {
                return repoRoot.FullName;
            }

            // Finally, try walking up from the current directory since the test assembly is sometimes
            // placed in a temp location due to shadow copy functionality in test runners like xunit
            repoRoot = GetRepositoryRootFromStartingDirectory(new DirectoryInfo(Directory.GetCurrentDirectory()));

            return repoRoot?.FullName;
        }

        /// <summary>
        /// Walks up the specified directory looking for NuGet.sln.
        /// </summary>
        /// <param name="startingDirectory">The <see cref="DirectoryInfo" /> to use as a starting directory.</param>
        /// <returns>A <see cref="DirectoryInfo" /> representing the root of the respository if one is found, otherwise <see langword="null" />.</returns>
        private static DirectoryInfo GetRepositoryRootFromStartingDirectory(DirectoryInfo startingDirectory)
        {
            DirectoryInfo currentDir = startingDirectory;

            while (currentDir != null)
            {
                string candidatePath = Path.Combine(currentDir.FullName, "NuGet.sln");

                if (File.Exists(candidatePath))
                {
                    // We have found the repo root.
                    return currentDir;
                }

                currentDir = currentDir.Parent;
            }

            return null;
        }

        public static string GetDotnetCli()
        {
            DirectoryInfo dir = GetDirectoryOfPathAbove(Path.Combine(".test", "dotnet"));

            if (dir != null)
            {
                var dotnetCli = Path.Combine(dir.FullName, DotnetCliExe);
                if (File.Exists(dotnetCli))
                {
                    return dotnetCli;
                }

                dotnetCli = Path.Combine(dir.FullName, DotnetCliBinary);
                if (File.Exists(dotnetCli))
                {
                    return dotnetCli;
                }
            }

            if (dir == null)
            {
                throw new Exception("Failed to determine the path to the .NET SDK");
            }

            return null;
        }

        public static string GetArtifactsDirectoryInRepo()
        {
            return GetDirectoryOfPathAbove("artifacts")?.FullName;
        }

        public static string GetNuGetExeDirectoryInRepo()
        {
            var ArtifactDir = GetArtifactsDirectoryInRepo();

            return Path.Combine(ArtifactDir, "VS15");
        }

        public static bool DeleteRandomTestFolder(string randomTestPath)
        {
            // Avoid cleaning up test folders if
            if (!SkipCleanupLazy.Value && Directory.Exists(randomTestPath))
            {
                AssertNotTempPath(randomTestPath);

                try
                {
                    Directory.Delete(randomTestPath, recursive: true);
                }
                catch
                {
                    // Failure
                    return false;
                }
            }

            // Success or skipped
            return true;
        }

        public static void AssertNotTempPath(string path)
        {
            var expanded = Path.GetFullPath(path).TrimEnd(new char[] { '\\', '/' });
            var expandedTempPath = Path.GetFullPath(Path.GetTempPath()).TrimEnd(new char[] { '\\', '/' });

            if (expanded.Equals(expandedTempPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Trying to delete the temp folder in a test");
            }

            if (expanded.Equals(Path.GetFullPath(NuGetTestFolder), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Trying to delete the root test folder in a test");
            }
        }

        public static DirectoryInfo GetDirectoryOfPathAbove(string relativePath)
        {
            var currentDirInfo = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (currentDirInfo != null)
            {
                DirectoryInfo candidateDir = new DirectoryInfo(Path.Combine(currentDirInfo.FullName, relativePath));

                if (candidateDir.Exists)
                {
                    return candidateDir;
                }

                currentDirInfo = currentDirInfo.Parent;
            }

            return null;
        }
    }
}
