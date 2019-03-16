// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.E2ETesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ProjectTemplates.Tests.Helpers;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Templates.Test.Helpers;
using Xunit;
using Xunit.Abstractions;

// Turn off parallel test run for Edge as the driver does not support multiple Selenium tests at the same time
#if EDGE
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
#endif
[assembly: TestFramework("Microsoft.AspNetCore.E2ETesting.XunitTestFrameworkWithAssemblyFixture", "ProjectTemplates.Tests")]
namespace Templates.Test.SpaTemplateTest
{
    public class SpaTemplateTestBase : BrowserTestBase
    {
        public SpaTemplateTestBase(
            ProjectFactoryFixture projectFactory, BrowserFixture browserFixture, ITestOutputHelper output) : base(browserFixture, output)
        {
            ProjectFactory = projectFactory;
        }

        public ProjectFactoryFixture ProjectFactory { get; set; }

        public Project Project { get; set; }

        // Rather than using [Theory] to pass each of the different values for 'template',
        // it's important to distribute the SPA template tests over different test classes
        // so they can be run in parallel. Xunit doesn't parallelize within a test class.
        protected async Task SpaTemplateImplAsync(
            string key,
            string template,
            bool useLocalDb = false,
            bool usesAuth = false)
        {
            Project = ProjectFactory.CreateProject(key, Output);

            var createResult = await Project.RunDotNetNewAsync(template, auth: usesAuth ? "Individual" : null, language: null, useLocalDb);
            Assert.True(0 == createResult.ExitCode, createResult.GetFormattedOutput());

            // We shouldn't have to do the NPM restore in tests because it should happen
            // automatically at build time, but by doing it up front we can avoid having
            // multiple NPM installs run concurrently which otherwise causes errors when
            // tests run in parallel.
            var clientAppSubdirPath = Path.Combine(Project.TemplateOutputDir, "ClientApp");
            Assert.True(File.Exists(Path.Combine(clientAppSubdirPath, "package.json")), "Missing a package.json");

            var projectFileContents = Project.ReadFile($"{Project.ProjectName}.csproj");
            if (usesAuth && !useLocalDb)
            {
                Assert.Contains(".db", projectFileContents);
            }

            var npmRestoreResult = await Npm.RestoreWithRetryAsync(Output, clientAppSubdirPath);
            Assert.True(0 == npmRestoreResult.ExitCode, npmRestoreResult.GetFormattedOutput());

            var lintResult = await ProcessEx.RunViaShellAsync(Output, clientAppSubdirPath, "npm run lint");
            Assert.True(0 == lintResult.ExitCode, lintResult.GetFormattedOutput());

            if (template == "react" || template == "reactredux")
            {
                var testResult = await ProcessEx.RunViaShellAsync(Output, clientAppSubdirPath, "npm run test");
                Assert.True(0 == testResult.ExitCode, testResult.GetFormattedOutput());
            }

            var publishResult = await Project.RunDotNetPublishAsync();
            Assert.True(0 == publishResult.ExitCode, publishResult.GetFormattedOutput());

            // Run dotnet build after publish. The reason is that one uses Config = Debug and the other uses Config = Release
            // The output from publish will go into bin/Release/netcoreapp3.0/publish and won't be affected by calling build
            // later, while the opposite is not true.

            var buildResult = await Project.RunDotNetBuildAsync();
            Assert.True(0 == buildResult.ExitCode, buildResult.GetFormattedOutput());

            if (usesAuth)
            {
                var migrationsResult = await Project.RunDotNetEfCreateMigrationAsync(template);
                Assert.True(0 == migrationsResult.ExitCode, migrationsResult.GetFormattedOutput());
                Project.AssertEmptyMigration(template);
            }

            using (var aspNetProcess = Project.StartBuiltProjectAsync())
            {
                await aspNetProcess.AssertStatusCode("/", HttpStatusCode.OK, "text/html");

                if (BrowserFixture.IsHostAutomationSupported())
                {
                    aspNetProcess.VisitInBrowser(Browser);
                    TestBasicNavigation(visitFetchData: !usesAuth);
                }
            }

            if (usesAuth)
            {
                UpdatePublishedSettings();
            }

            using (var aspNetProcess = Project.StartPublishedProjectAsync())
            {
                await aspNetProcess.AssertStatusCode("/", HttpStatusCode.OK, "text/html");

                if (BrowserFixture.IsHostAutomationSupported())
                {
                    aspNetProcess.VisitInBrowser(Browser);
                    TestBasicNavigation(visitFetchData: !usesAuth);
                }
            }
        }

        private void UpdatePublishedSettings()
        {
            // Hijack here the config file to use the development key during publish.
            var appSettings = JObject.Parse(File.ReadAllText(Path.Combine(Project.TemplateOutputDir, "appsettings.json")));
            var appSettingsDevelopment = JObject.Parse(File.ReadAllText(Path.Combine(Project.TemplateOutputDir, "appsettings.Development.json")));
            ((JObject)appSettings["IdentityServer"]).Merge(appSettingsDevelopment["IdentityServer"]);
            ((JObject)appSettings["IdentityServer"]).Merge(new
            {
                IdentityServer = new
                {
                    Key = new
                    {
                        FilePath = "./tempkey.json"
                    }
                }
            });
            var testAppSettings = appSettings.ToString();
            File.WriteAllText(Path.Combine(Project.TemplatePublishDir, "appsettings.json"), testAppSettings);
        }

        private void TestBasicNavigation(bool visitFetchData)
        {
            Browser.WaitForElement("ul");
            // <title> element gets project ID injected into it during template execution
            Assert.Contains(Project.ProjectGuid, Browser.Title);

            // Initially displays the home page
            Assert.Equal("Hello, world!", Browser.GetText("h1"));

            // Can navigate to the counter page
            Browser.Click(By.PartialLinkText("Counter"));
            Browser.WaitForUrl("counter");

            Assert.Equal("Counter", Browser.GetText("h1"));

            // Clicking the counter button works
            var counterComponent = Browser.FindElement("h1").Parent();
            Assert.Equal("0", counterComponent.GetText("strong"));
            Browser.Click(counterComponent, "button");
            Assert.Equal("1", counterComponent.GetText("strong"));

            if (visitFetchData)
            {
                // Can navigate to the 'fetch data' page
                Browser.Click(By.PartialLinkText("Fetch data"));
                Browser.WaitForUrl("fetch-data");
                Assert.Equal("Weather forecast", Browser.GetText("h1"));

                // Asynchronously loads and displays the table of weather forecasts
                var fetchDataComponent = Browser.FindElement("h1").Parent();
                Browser.WaitForElement("table>tbody>tr");
                var table = Browser.FindElement(fetchDataComponent, "table", timeoutSeconds: 5);
                Assert.Equal(5, table.FindElements(By.CssSelector("tbody tr")).Count);
            }
        }
    }
}
