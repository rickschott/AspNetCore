// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test
{
    public class MvcTemplateTest
    {
        public MvcTemplateTest(ProjectFactoryFixture projectFactory, ITestOutputHelper output)
        {
            ProjectFactory = projectFactory;
            Output = output;
        }

        public Project Project { get; set; }

        public ProjectFactoryFixture ProjectFactory { get; }
        public ITestOutputHelper Output { get; }

        [Theory]
        [InlineData(null)]
        [InlineData("F#")]
        public async Task MvcTemplate_NoAuthImplAsync(string languageOverride)
        {
            Project = ProjectFactory.CreateProject("mvcnoauth" + (languageOverride == "F#" ? "fsharp" : "csharp"), Output);

            var createResult = await Project.RunDotNetNewAsync("mvc", language: languageOverride);
            Assert.True(0 == createResult.ExitCode, createResult.GetFormattedOutput());

            Project.AssertDirectoryExists("Areas", false);
            Project.AssertDirectoryExists("Extensions", false);
            Project.AssertFileExists("urlRewrite.config", false);
            Project.AssertFileExists("Controllers/AccountController.cs", false);

            var projectExtension = languageOverride == "F#" ? "fsproj" : "csproj";
            var projectFileContents = Project.ReadFile($"{Project.ProjectName}.{projectExtension}");
            Assert.DoesNotContain(".db", projectFileContents);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Tools", projectFileContents);
            Assert.DoesNotContain("Microsoft.VisualStudio.Web.CodeGeneration.Design", projectFileContents);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Tools.DotNet", projectFileContents);
            Assert.DoesNotContain("Microsoft.Extensions.SecretManager.Tools", projectFileContents);

            var publishResult = await Project.RunDotNetPublishAsync();
            Assert.True(0 == publishResult.ExitCode, publishResult.GetFormattedOutput());

            // Run dotnet build after publish. The reason is that one uses Config = Debug and the other uses Config = Release
            // The output from publish will go into bin/Release/netcoreapp3.0/publish and won't be affected by calling build
            // later, while the opposite is not true.

            var buildResult = await Project.RunDotNetBuildAsync();
            Assert.True(0 == buildResult.ExitCode, buildResult.GetFormattedOutput());

            using (var aspNetProcess = Project.StartBuiltProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Home/Privacy");
            }

            using (var aspNetProcess = Project.StartPublishedProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Home/Privacy");
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MvcTemplate_IndividualAuthImplAsync(bool useLocalDB)
        {
            Project = ProjectFactory.CreateProject("mvcindividual" + (useLocalDB ? "uld" : ""), Output);

            var createResult = await Project.RunDotNetNewAsync("mvc", auth: "Individual", useLocalDB: useLocalDB);
            Assert.True(0 == createResult.ExitCode, createResult.GetFormattedOutput());

            Project.AssertDirectoryExists("Extensions", false);
            Project.AssertFileExists("urlRewrite.config", false);
            Project.AssertFileExists("Controllers/AccountController.cs", false);

            var projectFileContents = Project.ReadFile($"{Project.ProjectName}.csproj");
            if (!useLocalDB)
            {
                Assert.Contains(".db", projectFileContents);
            }

            var publishResult = await Project.RunDotNetPublishAsync();
            Assert.True(0 == publishResult.ExitCode, publishResult.GetFormattedOutput());

            // Run dotnet build after publish. The reason is that one uses Config = Debug and the other uses Config = Release
            // The output from publish will go into bin/Release/netcoreapp3.0/publish and won't be affected by calling build
            // later, while the opposite is not true.

            var buildResult = await Project.RunDotNetBuildAsync();
            Assert.True(0 == buildResult.ExitCode, buildResult.GetFormattedOutput());

            var migrationsResult = await Project.RunDotNetEfCreateMigrationAsync("mvc");
            Assert.True(0 == migrationsResult.ExitCode, migrationsResult.GetFormattedOutput());
            Project.AssertEmptyMigration("mvc");

            using (var aspNetProcess = Project.StartBuiltProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Identity/Account/Login");
                await aspNetProcess.AssertOk("/Home/Privacy");
            }

            using (var aspNetProcess = Project.StartPublishedProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Identity/Account/Login");
                await aspNetProcess.AssertOk("/Home/Privacy");
            }
        }
    }
}
