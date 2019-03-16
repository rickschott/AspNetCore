// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;
using Templates.Test.Helpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace ProjectTemplates.Tests.Helpers
{
    public class Project
    {
        public const string DefaultFramework = "netcoreapp3.0";

        public SemaphoreSlim DotNetNewLock { get; set; }

        public string ProjectName { get; set; }
        public string ProjectGuid { get; set; }
        public string TemplateOutputDir { get; set; }
        public string TemplateBuildDir => Path.Combine(TemplateOutputDir, "bin", "Debug", DefaultFramework);
        public string TemplatePublishDir => Path.Combine(TemplateOutputDir, "bin", "Release", DefaultFramework, "publish");

        public ITestOutputHelper Output { get; set; }
        public IMessageSink DiagnosticsMessageSink { get; set; }

        internal async Task<ProcessEx> RunDotNetNewAsync(string templateName, string auth = null, string language = null, bool useLocalDB = false, bool noHttps = false)
        {
            var args = $"new {templateName} --debug:custom-hive \"{TemplatePackageInstaller.CustomHivePath}\"";

            if (!string.IsNullOrEmpty(auth))
            {
                args += $" --auth {auth}";
            }

            if (!string.IsNullOrEmpty(language))
            {
                args += $" -lang {language}";
            }

            if (useLocalDB)
            {
                args += $" --use-local-db";
            }

            if (noHttps)
            {
                args += $" --no-https";
            }

            args += $" -o {TemplateOutputDir}";

            // Only run one instance of 'dotnet new' at once, as a workaround for
            // https://github.com/aspnet/templating/issues/63

            await DotNetNewLock.WaitAsync();

            try
            {
                var execution = ProcessEx.Run(Output, AppContext.BaseDirectory, DotNetMuxer.MuxerPathOrDefault(), args);
                await execution.Exited;
                return execution;
            }
            finally
            {
                DotNetNewLock.Release();
            }
        }

        internal async Task<ProcessEx> RunDotNetPublishAsync()
        {
            Output.WriteLine("Publishing ASP.NET application...");

            // Workaround for issue with runtime store not yet being published
            // https://github.com/aspnet/Home/issues/2254#issuecomment-339709628
            var extraArgs = "-p:PublishWithAspNetCoreTargetManifest=false";

            // This is going to trigger a build, so we need to acquire the lock like in the other cases. At least for NPM related things.
            await DotNetNewLock.WaitAsync();
            try
            {
                var result = ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), $"publish -c Release {extraArgs}");
                await result.Exited;
                return result;
            }
            finally
            {
                DotNetNewLock.Release();
            }
        }

        internal async Task<ProcessEx> RunDotNetBuildAsync()
        {
            Output.WriteLine("Building ASP.NET application...");

            // This is going to trigger a build, so we need to acquire the lock like in the other cases. At least for NPM related things.
            await DotNetNewLock.WaitAsync();
            try
            {
                var result = ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), "build -c Debug");
                await result.Exited;
                return result;
            }
            finally
            {
                DotNetNewLock.Release();
            }
        }

        internal AspNetProcess StartBuiltProjectAsync()
        {
            var environment = new Dictionary<string, string>
            {
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:0;https://127.0.0.1:0",
                ["ASPNETCORE_ENVIRONMENT"] = "Development"
            };

            var projectDll = Path.Combine(TemplateBuildDir, $"{ProjectName}.dll");
            return new AspNetProcess(Output, TemplateOutputDir, projectDll, environment);
        }

        internal AspNetProcess StartPublishedProjectAsync()
        {
            var environment = new Dictionary<string, string>
            {
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:0;https://127.0.0.1:0",
            };

            var projectDll = $"{ProjectName}.dll";
            return new AspNetProcess(Output, TemplatePublishDir, projectDll, environment);
        }

        internal async Task<ProcessEx> RunDotNetEfCreateMigrationAsync(string migrationName)
        {
            var assembly = typeof(ProjectFactoryFixture).Assembly;

            var dotNetEfFullPath = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(attribute => attribute.Key == "DotNetEfFullPath")
                .Value;

            var args = $"\"{dotNetEfFullPath}\" --verbose --no-build migrations add {migrationName}";

            // Only run one instance of 'dotnet new' at once, as a workaround for
            // https://github.com/aspnet/templating/issues/63
            await DotNetNewLock.WaitAsync();
            try
            {
                var result = ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), args);
                await result.Exited;
                return result;
            }
            finally
            {
                DotNetNewLock.Release();
            }
        }

        // If this fails, you should generate new migrations via migrations/updateMigrations.cmd
        public void AssertEmptyMigration(string migration)
        {
            var fullPath = Path.Combine(TemplateOutputDir, "Data/Migrations");
            var file = Directory.EnumerateFiles(fullPath).Where(f => f.EndsWith($"{migration}.cs")).FirstOrDefault();

            Assert.NotNull(file);
            var contents = File.ReadAllText(file);

            var emptyMigration = @"protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }";

            // This comparison can break depending on how GIT checked out newlines on different files.
            Assert.Contains(RemoveNewLines(emptyMigration), RemoveNewLines(contents));

            static string RemoveNewLines(string str)
            {
                return str.Replace("\n", string.Empty).Replace("\r", string.Empty);
            }
        }

        internal async Task<ProcessEx> RunDotNetNewRawAsync(string arguments)
        {
            await DotNetNewLock.WaitAsync();
            try
            {
                var result = ProcessEx.Run(
                    Output,
                    AppContext.BaseDirectory,
                    DotNetMuxer.MuxerPathOrDefault(),
                    arguments +
                        $" --debug:custom-hive \"{TemplatePackageInstaller.CustomHivePath}\"" +
                        $" -o {TemplateOutputDir}");
                await result.Exited;
                return result;
            }
            finally
            {
                DotNetNewLock.Release();
            }
        }

        public void Dispose()
        {
            DeleteOutputDirectory();
        }

        public void DeleteOutputDirectory()
        {
            const int NumAttempts = 10;

            for (var numAttemptsRemaining = NumAttempts; numAttemptsRemaining > 0; numAttemptsRemaining--)
            {
                try
                {
                    Directory.Delete(TemplateOutputDir, true);
                    return;
                }
                catch (Exception ex)
                {
                    if (numAttemptsRemaining > 1)
                    {
                        DiagnosticsMessageSink.OnMessage(new DiagnosticMessage($"Failed to delete directory {TemplateOutputDir} because of error {ex.Message}. Will try again {numAttemptsRemaining - 1} more time(s)."));
                        Thread.Sleep(3000);
                    }
                    else
                    {
                        DiagnosticsMessageSink.OnMessage(new DiagnosticMessage($"Giving up trying to delete directory {TemplateOutputDir} after {NumAttempts} attempts. Most recent error was: {ex.StackTrace}"));
                    }
                }
            }
        }
    }
}
