// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Internal;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.E2ETesting
{
    class SeleniumStandaloneServer
    {
        private static SeleniumStandaloneServer _instance = null;
        private static Task<SeleniumStandaloneServer> _instanceTask = null;
        private static object _lock = new object();

        public SeleniumStandaloneServer(Uri uri)
        {
            Uri = uri;
        }

        public Uri Uri { get; }

        public static async Task<SeleniumStandaloneServer> GetInstanceAsync(ITestOutputHelper output)
        {
            lock (_lock)
            {
                if (_instanceTask == null)
                {
                    _instanceTask = CreateInstance(output);
                }
            }

            var instance = await _instanceTask;
            lock (_lock)
            {
                _instance = instance;
            }

            return _instance;
        }

        private static async Task<SeleniumStandaloneServer> CreateInstance(ITestOutputHelper output)
        {
            var port = FindAvailablePort();
            var uri = new UriBuilder("http", "localhost", port, "/wd/hub").Uri;

            var psi = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = $"run selenium-standalone start -- -- -port {port}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "cmd";
                psi.Arguments = $"/c npm {psi.Arguments}";
            }

            var process = Process.Start(psi);

            process.OutputDataReceived += LogOutput;
            process.ErrorDataReceived += LogOutput;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // The Selenium sever has to be up for the entirety of the tests and is only shutdown when the application (i.e. the test) exits.
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                if (!process.HasExited)
                {
                    process.KillTree(TimeSpan.FromSeconds(10));
                    process.Dispose();
                }
            };

            void LogOutput(object sender, DataReceivedEventArgs e)
            {
                lock (output)
                {
                    output.WriteLine(e.Data);
                }
            }

            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(1),
            };

            var retries = 0;
            while (retries++ < 30)
            {
                try
                {
                    var response = await httpClient.GetAsync(uri);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return new SeleniumStandaloneServer(uri);
                    }
                }
                catch (OperationCanceledException)
                {
                }

                await Task.Delay(1000);
            }

            throw new Exception("Failed to launch the server");
        }

        static int FindAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
