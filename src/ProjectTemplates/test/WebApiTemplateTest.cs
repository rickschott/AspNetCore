// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test
{
    public class WebApiTemplateTest
    {
        public WebApiTemplateTest(ProjectFactoryFixture factoryFixture, ITestOutputHelper output)
        {
            FactoryFixture = factoryFixture;
            Output = output;
        }

        public ProjectFactoryFixture FactoryFixture { get; }

        public ITestOutputHelper Output { get; }

        public Project Project { get; set; }

        [Fact]
        public async Task WebApiTemplateAsync()
        {
            Project = FactoryFixture.GetOrCreateProject("webapi", Output);

            var createResult = await Project.RunDotNetNewAsync("webapi");
            Assert.True(0 == createResult.ExitCode, createResult.GetFormattedOutput());

            var publishResult = await Project.RunDotNetPublishAsync();
            Assert.True(0 == publishResult.ExitCode, publishResult.GetFormattedOutput());

            // Run dotnet build after publish. The reason is that one uses Config = Debug and the other uses Config = Release
            // The output from publish will go into bin/Release/netcoreapp3.0/publish and won't be affected by calling build
            // later, while the opposite is not true.

            var buildResult = await Project.RunDotNetBuildAsync();
            Assert.True(0 == buildResult.ExitCode, buildResult.GetFormattedOutput());

            using (var aspNetProcess = Project.StartBuiltProjectAsync())
            {
                await aspNetProcess.AssertOk("/api/values");
                await aspNetProcess.AssertNotFound("/");
            }

            using (var aspNetProcess = Project.StartPublishedProjectAsync())
            {
                await aspNetProcess.AssertOk("/api/values");
                await aspNetProcess.AssertNotFound("/");
            }
        }
    }
}
