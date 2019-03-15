// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test
{
    public class EmptyWebTemplateTest
    {
        public EmptyWebTemplateTest(ProjectFactoryFixture projectFactory, ITestOutputHelper output)
        {
            Project = projectFactory.CreateProject(output);
        }

        public Project Project { get; }

        [Fact]
        public async Task EmptyWebTemplateAsync()
        {
            Project.RunDotNetNew("web");

            foreach (var publish in new[] { false, true })
            {
                using (var aspNetProcess = Project.StartAspNetProcess(publish))
                {
                    await aspNetProcess.AssertOk("/");
                }
            }
        }
    }
}
