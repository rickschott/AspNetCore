// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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

        private ConcurrentBag<Project> _projects = new ConcurrentBag<Project>();

        public IMessageSink DiagnosticsMessageSink { get; }

        public ProjectFactoryFixture(IMessageSink diagnosticsMessageSink)
        {
            DiagnosticsMessageSink = diagnosticsMessageSink;
        }

        public Project CreateProject(ITestOutputHelper output)
        {
            TemplatePackageInstaller.EnsureTemplatingEngineInitialized(output);
            var project = new Project
            {
                DotNetNewLock = DotNetNewLock,
                Output = output,
                DiagnosticsMessageSink = DiagnosticsMessageSink,
                ProjectGuid = Guid.NewGuid().ToString("N").Substring(0, 6)
            };
            project.ProjectName = $"AspNet.Template.{project.ProjectGuid}";

            _projects.Add(project);

            var assemblyPath = GetType().Assembly;
            string basePath = GetTemplateFolderBasePath(assemblyPath);
            project.TemplateOutputDir = Path.Combine(basePath, project.ProjectName);

            return project;
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
                    project.Dispose();
                }
                catch(Exception e)
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
        public ITestOutputHelper Output { get; set; }
        public object DotNetNewLock { get; set; }
        public IMessageSink DiagnosticsMessageSink { get; set; }

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

        public void RunDotNet(string arguments)
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
