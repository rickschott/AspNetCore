// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.E2ETesting
{
    [CaptureSeleniumLogs]
    public class BrowserTestBase : IClassFixture<BrowserFixture>, IAsyncLifetime
    {
        private static readonly AsyncLocal<IWebDriver> _asyncBrowser = new AsyncLocal<IWebDriver>();
        private static readonly AsyncLocal<ILogs> _logs = new AsyncLocal<ILogs>();
        private static readonly AsyncLocal<ITestOutputHelper> _output = new AsyncLocal<ITestOutputHelper>();

        public BrowserTestBase(BrowserFixture browserFixture, ITestOutputHelper output)
        {
            Fixture = browserFixture;
            _output.Value = output;
        }

        public IWebDriver Browser { get; set; }

        public static IWebDriver BrowserAccessor => _asyncBrowser.Value;

        public static ILogs Logs => _logs.Value;

        public static ITestOutputHelper Output => _output.Value;

        public BrowserFixture Fixture { get; }

        public Task DisposeAsync()
        {
            _asyncBrowser.Value?.Dispose();
            return Task.CompletedTask;
        }

        public  async Task InitializeAsync()
        {
            var (browser, logs) = await Fixture.GetOrCreateBrowserAsync(Output);
            _asyncBrowser.Value = browser;
            _logs.Value = logs;

            Browser = browser;
        }
    }
}
