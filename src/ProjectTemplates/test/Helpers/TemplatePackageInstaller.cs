// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.CommandLineUtils;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test.Helpers
{
    internal static class TemplatePackageInstaller
    {
        private static object _templatePackagesReinstallationLock = new object();
        private static bool _haveReinstalledTemplatePackages;

        private static object DotNetNewLock = new object();

        private static readonly string[] _templatePackages = new[]
        {
            "Microsoft.DotNet.Common.ItemTemplates",
            "Microsoft.DotNet.Common.ProjectTemplates.2.1",
            "Microsoft.DotNet.Test.ProjectTemplates.2.1",
            "Microsoft.DotNet.Web.Client.ItemTemplates",
            "Microsoft.DotNet.Web.ItemTemplates",
            "Microsoft.DotNet.Web.ProjectTemplates.1.x",
            "Microsoft.DotNet.Web.ProjectTemplates.2.0",
            "Microsoft.DotNet.Web.ProjectTemplates.2.1",
            "Microsoft.DotNet.Web.ProjectTemplates.2.2",
            "Microsoft.DotNet.Web.ProjectTemplates.3.0",
            "Microsoft.DotNet.Web.Spa.ProjectTemplates",
            "Microsoft.DotNet.Web.Spa.ProjectTemplates.2.2",
            "Microsoft.DotNet.Web.Spa.ProjectTemplates.3.0"
        };

        public static string CustomHivePath { get; } = typeof(TemplatePackageInstaller)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(s => s.Key == "CustomTemplateHivePath").Value;

        public static void EnsureTemplatingEngineInitialized(ITestOutputHelper output)
        {
            lock (_templatePackagesReinstallationLock)
            {
                if (!_haveReinstalledTemplatePackages)
                {
                    if (Directory.Exists(CustomHivePath))
                    {
                        Directory.Delete(CustomHivePath, recursive: true);
                    }
                    InstallTemplatePackages(output);
                    _haveReinstalledTemplatePackages = true;
                }
            }
        }

        public static ProcessEx RunDotNetNew(ITestOutputHelper output, string arguments, bool assertSuccess)
        {
            lock (DotNetNewLock)
            {
                var proc = ProcessEx.Run(
                    output,
                    AppContext.BaseDirectory,
                    DotNetMuxer.MuxerPathOrDefault(),
                    $"new {arguments} --debug:custom-hive \"{CustomHivePath}\"");
                proc.WaitForExit(assertSuccess);

                return proc;
            }
        }

        private static void InstallTemplatePackages(ITestOutputHelper output)
        {
            var builtPackages = Directory.EnumerateFiles(
                    typeof(TemplatePackageInstaller).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(a => a.Key == "ArtifactsShippingPackagesDir").Value,
                    "*.nupkg")
                .Where(p => _templatePackages.Any(t => Path.GetFileName(p).StartsWith(t, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.Equal(4, builtPackages.Length);

            // Remove any previous or prebundled version of the template packages
            foreach (var packageName in _templatePackages)
            {
                // We don't need this command to succeed, because we'll verify next that
                // uninstallation had the desired effect. This command is expected to fail
                // in the case where the package wasn't previously installed.
                RunDotNetNew(output, $"--uninstall {packageName}", assertSuccess: false);
            }

            VerifyCannotFindTemplate(output, "web");
            VerifyCannotFindTemplate(output, "webapp");
            VerifyCannotFindTemplate(output, "mvc");
            VerifyCannotFindTemplate(output, "react");
            VerifyCannotFindTemplate(output, "reactredux");
            VerifyCannotFindTemplate(output, "angular");

            foreach (var packagePath in builtPackages)
            {
                output.WriteLine($"Installing templates package {packagePath}...");
                RunDotNetNew(output, $"--install \"{packagePath}\"", assertSuccess: true);
            }
            VerifyCanFindTemplate(output, "webapp");
            VerifyCanFindTemplate(output, "web");
            VerifyCanFindTemplate(output, "react");
        }

        private static void VerifyCanFindTemplate(ITestOutputHelper output, string templateName)
        {
            var proc = RunDotNetNew(output, $"", assertSuccess: false);
            if (!proc.Output.Contains($" {templateName} "))
            {
                throw new InvalidOperationException($"Couldn't find {templateName} as an option in {proc.Output}.");
            }
        }

        private static void VerifyCannotFindTemplate(ITestOutputHelper output, string templateName)
        {
            // Verify we really did remove the previous templates
            var tempDir = Path.Combine(AppContext.BaseDirectory, Path.GetRandomFileName(), Guid.NewGuid().ToString("D"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var proc = RunDotNetNew(output, $"\"{templateName}\"", assertSuccess: false);

                if (!proc.Error.Contains($"No templates matched the input template name: {templateName}."))
                {
                    throw new InvalidOperationException($"Failed to uninstall previous templates. The template '{templateName}' could still be found.");
                }
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
