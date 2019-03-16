// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
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
    public class ProjectFactoryFixture : IDisposable
    {
        private static object DotNetNewLock = new object();
        private static SemaphoreSlim _asyncSemaphore = new SemaphoreSlim(1);

        private ConcurrentDictionary<string, Project> _projects = new ConcurrentDictionary<string, Project>();

        public IMessageSink DiagnosticsMessageSink { get; }

        public ProjectFactoryFixture(IMessageSink diagnosticsMessageSink)
        {
            DiagnosticsMessageSink = diagnosticsMessageSink;
        }

        public Project CreateProject(string projectKey, ITestOutputHelper output)
        {
            TemplatePackageInstaller.EnsureTemplatingEngineInitialized(output);
            return _projects.GetOrAdd(projectKey, (key, outputHelper) =>
             {
                 var project = new Project
                 {
                     DotNetNewLock = DotNetNewLock,
                     Semaphore = _asyncSemaphore,
                     Output = outputHelper,
                     DiagnosticsMessageSink = DiagnosticsMessageSink,
                     ProjectGuid = Guid.NewGuid().ToString("N").Substring(0, 6)
                 };
                 project.ProjectName = $"AspNet.{key}.{project.ProjectGuid}";

                 var assemblyPath = GetType().Assembly;
                 string basePath = GetTemplateFolderBasePath(assemblyPath);
                 project.TemplateOutputDir = Path.Combine(basePath, project.ProjectName);
                 return project;
             },
             output);
        }

        private static string GetTemplateFolderBasePath(Assembly assembly) =>
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "TestTemplateCreationFolder")
                .Value;

        public void Dispose()
        {
            var list = new List<Exception>();
            foreach (var project in _projects)
            {
                try
                {
                    project.Value.Dispose();
                }
                catch (Exception e)
                {
                    list.Add(e);
                }
            }

            if (list.Count > 0)
            {
                throw new AggregateException(list);
            }
        }
    }

    public class Project
    {
        public string ProjectName { get; set; }
        public string ProjectGuid { get; set; }
        public string TemplateOutputDir { get; set; }

        public string TemplateBuildDir =>
            Path.Combine(TemplateOutputDir, "bin", "Debug", AspNetProcess.DefaultFramework);

        public string TemplatePublishDir =>
            Path.Combine(TemplateOutputDir, "bin", "Release", AspNetProcess.DefaultFramework, "publish");

        public ITestOutputHelper Output { get; set; }
        public object DotNetNewLock { get; set; }
        public IMessageSink DiagnosticsMessageSink { get; set; }
        public SemaphoreSlim Semaphore { get; set; }

        public void RunDotNetNew(string templateName, string auth = null, string language = null, bool useLocalDB = false, bool noHttps = false)
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
            lock (DotNetNewLock)
            {
                ProcessEx.Run(Output, AppContext.BaseDirectory, DotNetMuxer.MuxerPathOrDefault(), args).WaitForExit(assertSuccess: true);
            }
        }

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

            await Semaphore.WaitAsync();

            try
            {
                var execution = ProcessEx.Run(Output, AppContext.BaseDirectory, DotNetMuxer.MuxerPathOrDefault(), args);
                await execution.Exited;
                return execution;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task<ProcessEx> RunDotNetPublishAsync()
        {
            Output.WriteLine("Publishing ASP.NET application...");

            // Workaround for issue with runtime store not yet being published
            // https://github.com/aspnet/Home/issues/2254#issuecomment-339709628
            var extraArgs = "-p:PublishWithAspNetCoreTargetManifest=false";

            // This is going to trigger a build, so we need to acquire the lock like in the other cases. At least for NPM related things.
            await Semaphore.WaitAsync();
            try
            {
                var result = ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), $"publish -c Release {extraArgs}");
                await result.Exited;
                return result;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        internal async Task<ProcessEx> RunDotNetBuildAsync()
        {
            Output.WriteLine("Building ASP.NET application...");

            // This is going to trigger a build, so we need to acquire the lock like in the other cases. At least for NPM related things.
            await Semaphore.WaitAsync();
            try
            {
                var result = ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), "build -c Debug");
                await result.Exited;
                return result;
            }
            finally
            {
                Semaphore.Release();
            }
        }


        public void RunDotNetNewRaw(string arguments)
        {
            lock (DotNetNewLock)
            {
                ProcessEx.Run(
                    Output,
                    AppContext.BaseDirectory,
                    DotNetMuxer.MuxerPathOrDefault(),
                    arguments +
                        $" --debug:custom-hive \"{TemplatePackageInstaller.CustomHivePath}\"" +
                        $" -o {TemplateOutputDir}")
                .WaitForExit(assertSuccess: true);
            }
        }

        public void RunDotNetEfCreateMigration(string migrationName)
        {
            var assembly = typeof(ProjectFactoryFixture).Assembly;

            var dotNetEfFullPath = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(attribute => attribute.Key == "DotNetEfFullPath")
                .Value;

            var args = $"\"{dotNetEfFullPath}\" --verbose migrations add {migrationName}";

            // Only run one instance of 'dotnet new' at once, as a workaround for
            // https://github.com/aspnet/templating/issues/63
            lock (DotNetNewLock)
            {
                ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), args).WaitForExit(assertSuccess: true);
            }
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
            await Semaphore.WaitAsync();
            try
            {
                var result = ProcessEx.Run(Output, TemplateOutputDir, DotNetMuxer.MuxerPathOrDefault(), args);
                await result.Exited;
                return result;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public void AssertDirectoryExists(string path, bool shouldExist)
        {
            var fullPath = Path.Combine(TemplateOutputDir, path);
            var doesExist = Directory.Exists(fullPath);

            if (shouldExist)
            {
                Assert.True(doesExist, "Expected directory to exist, but it doesn't: " + path);
            }
            else
            {
                Assert.False(doesExist, "Expected directory not to exist, but it does: " + path);
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
        }

        private static string RemoveNewLines(string str)
        {
            return str.Replace("\n", string.Empty).Replace("\r", string.Empty);
        }

        public void AssertFileExists(string path, bool shouldExist)
        {
            var fullPath = Path.Combine(TemplateOutputDir, path);
            var doesExist = File.Exists(fullPath);

            if (shouldExist)
            {
                Assert.True(doesExist, "Expected file to exist, but it doesn't: " + path);
            }
            else
            {
                Assert.False(doesExist, "Expected file not to exist, but it does: " + path);
            }
        }

        public string ReadFile(string path)
        {
            AssertFileExists(path, shouldExist: true);
            return File.ReadAllText(Path.Combine(TemplateOutputDir, path));
        }

        public AspNetProcess StartAspNetProcess(bool publish = false)
        {
            return new AspNetProcess(Output, TemplateOutputDir, ProjectName, publish);
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
