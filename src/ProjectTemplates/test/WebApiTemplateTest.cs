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
            Project = FactoryFixture.CreateProject("webapi", Output);

            Project.RunDotNetNew("webapi");

            foreach (var publish in new[] { false, true })
            {
                using (var aspNetProcess = Project.StartAspNetProcess(publish))
                {
                    await aspNetProcess.AssertOk("/api/values");
                    await aspNetProcess.AssertNotFound("/");
                }
            }
        }
    }
}
