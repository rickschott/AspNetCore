// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.E2ETesting;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test.SpaTemplateTest
{
    public class ReactTemplateTest : SpaTemplateTestBase
    {
        public ReactTemplateTest(ProjectFactoryFixture projectFactory, BrowserFixture browserFixture, ITestOutputHelper output)
            : base(projectFactory, browserFixture, output)
        {
        }

        //[Fact(Skip="This test is flaky. Using https://github.com/aspnet/AspNetCore-Internal/issues/1745 to track re-enabling this.")]
        [Fact]
        public Task ReactTemplate_Works_NetCore()
            => SpaTemplateImplAsync("reactnoauth", "react");

        [Fact]
        public Task ReactTemplate_IndividualAuth_NetCore()
            => SpaTemplateImpl_IndividualAuthAsync("reactindividual", "react");

        [Fact]
        public Task ReactTemplate_IndividualAuth_NetCore_LocalDb()
            => SpaTemplateImpl_IndividualAuthAsync("reactindividualuld", "react", useLocalDb: true);
    }
}
