// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.E2ETesting
{
    class SeleniumStandaloneServer
    {
        private static SeleniumStandaloneServer _instance = null;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        public SeleniumStandaloneServer(Uri uri)
        {
            Uri = uri;
        }

        public Uri Uri { get; }

        public static async Task<SeleniumStandaloneServer> GetInstanceAsync(ITestOutputHelper output)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_instance == null)
                {
                    _instance = await CreateInstance(output);
                }
            }
            finally
            {
                _semaphore.Release();
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

            // It's important that we get the folder value before we start the process to prevent
            // untracked processes when the tracking folder is not correctly configure.
            var trackingFolder = GetProcessTrackingFolder();
            if (!Directory.Exists(trackingFolder))
            {
                throw new InvalidOperationException($"Invalid tracking folder. Set the 'SeleniumProcessTrackingFolder' MSBuild property to a valid folder.");
            }

            var process = Process.Start(psi);
            await WriteTrackingFileAsync(output, trackingFolder, process);

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
            do
            {
                await Task.Delay(1000);
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

                retries++;
            } while (retries < 30);

            throw new Exception("Failed to launch the server");
        }

        private static async Task WriteTrackingFileAsync(ITestOutputHelper output, string trackingFolder, Process process)
        {
            var pidFile = Path.Combine(trackingFolder, $"{process.Id}.{Guid.NewGuid()}.pid");
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(pidFile, process.Id.ToString());
                    return;
                }
                catch
                {
                    output.WriteLine($"Can't write file to process tracking folder: {trackingFolder}");
                }
            }

            throw new InvalidOperationException($"Failed to write file for process {process.Id}");
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

        private static string GetProcessTrackingFolder() =>
            typeof(SeleniumStandaloneServer).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "Microsoft.AspNetCore.Testing.Selenium.ProcessTracking").Value;
    }
}
