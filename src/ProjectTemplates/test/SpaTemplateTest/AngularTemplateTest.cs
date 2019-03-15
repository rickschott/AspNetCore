// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.E2ETesting;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test.SpaTemplateTest
{
    public class AngularTemplateTest : SpaTemplateTestBase
    {
        public AngularTemplateTest(ProjectFactoryFixture projectFactory, BrowserFixture browserFixture, ITestOutputHelper output)
            : base(projectFactory, browserFixture, output) { }

        [Fact(Skip = "https://github.com/aspnet/AspNetCore-Internal/issues/1854")]
        public Task AngularTemplate_Works()
            => SpaTemplateImplAsync("angularnoauth", "angular");

        [Fact]
        public Task AngularTemplate_IndividualAuth_Works()
            => SpaTemplateImpl_IndividualAuthAsync("angularindividual", "angular");

        [Fact]
        public Task AngularTemplate_IndividualAuth_Works_LocalDb()
            => SpaTemplateImpl_IndividualAuthAsync("angularindividualuld", "angular", useLocalDb: true);
    }
}
