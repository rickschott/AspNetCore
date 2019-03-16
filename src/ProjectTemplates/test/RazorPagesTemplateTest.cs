// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Testing.xunit;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test
{
    public class RazorPagesTemplateTest
    {
        public RazorPagesTemplateTest(ProjectFactoryFixture projectFactory, ITestOutputHelper output)
        {
            ProjectFactory = projectFactory;
            Output = output;
        }

        public Project Project { get; set; }

        public ProjectFactoryFixture ProjectFactory { get; set; }

        public ITestOutputHelper Output { get; }

        [Fact]
        public async Task RazorPagesTemplate_NoAuthImplAsync()
        {
            Project = ProjectFactory.CreateProject("razorpagesnoauth", Output);

            var createResult = await Project.RunDotNetNewAsync("razor");
            Assert.True(0 == createResult.ExitCode, createResult.GetFormattedOutput());

            Project.AssertFileExists("Pages/Shared/_LoginPartial.cshtml", false);

            var projectFileContents = Project.ReadFile($"{Project.ProjectName}.csproj");
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
                await aspNetProcess.AssertOk("/Privacy");
            }

            using (var aspNetProcess = Project.StartPublishedProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Privacy");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RazorPagesTemplate_IndividualAuthImplAsync(bool useLocalDB)
        {
            Project = ProjectFactory.CreateProject("razorpagesindividual" + (useLocalDB ? "uld" : ""), Output);

            var createResult = await Project.RunDotNetNewAsync("razor", auth: "Individual", useLocalDB: useLocalDB);
            Assert.True(0 == createResult.ExitCode, createResult.GetFormattedOutput());

            Project.AssertFileExists("Pages/Shared/_LoginPartial.cshtml", true);

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

            var migrationsResult = await Project.RunDotNetEfCreateMigrationAsync("razorpages");
            Assert.True(0 == migrationsResult.ExitCode, migrationsResult.GetFormattedOutput());
            Project.AssertEmptyMigration("razorpages");

            using (var aspNetProcess = Project.StartBuiltProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Identity/Account/Login");
                await aspNetProcess.AssertOk("/Privacy");
            }

            using (var aspNetProcess = Project.StartPublishedProjectAsync())
            {
                await aspNetProcess.AssertOk("/");
                await aspNetProcess.AssertOk("/Identity/Account/Login");
                await aspNetProcess.AssertOk("/Privacy");
            }
        }
    }
}
