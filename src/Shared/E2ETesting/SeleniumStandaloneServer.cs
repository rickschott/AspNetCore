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
using Microsoft.AspNetCore.E2ETesting;
using Microsoft.Extensions.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.E2ETesting
{
    public class SeleniumStandaloneServer : IDisposable
    {
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        private Process _process;
        private string _sentinelPath;

        public SeleniumStandaloneServer()
        {
            if (Instance != null)
            {
                throw new InvalidOperationException("Selenium standalone singleton already created.");
            }

            // The assembly level attribute AssemblyFixture takes care of this being being instantiated before tests run
            // and disposed after tests are run, gracefully shutting down the server when possible by calling Dispose on
            // the singleton.
            Instance = this;
        }

        private void Initialize(
            Uri uri,
            Process process,
            string sentinelPath)
        {
            Uri = uri;
            _process = process;
            _sentinelPath = sentinelPath;
        }

        public Uri Uri { get; private set; }

        internal static SeleniumStandaloneServer Instance { get; private set; }

        public static async Task<SeleniumStandaloneServer> GetInstanceAsync(ITestOutputHelper output)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (Instance == null)
                {

                }
                if (Instance._process == null)
                {
                    // No process was started, meaning the instance wasn't initialized.
                    await InitializeInstance(output);
                }
            }
            finally
            {
                _semaphore.Release();
            }

            return Instance;
        }

        private static async Task InitializeInstance(ITestOutputHelper output)
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
            var pidFilePath = await WriteTrackingFileAsync(output, trackingFolder, process);

            process.OutputDataReceived += LogOutput;
            process.ErrorDataReceived += LogOutput;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // The Selenium sever has to be up for the entirety of the tests and is only shutdown when the application (i.e. the test) exits.
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => ProcessCleanup(process, pidFilePath);
            void LogOutput(object sender, DataReceivedEventArgs e)
            {
                lock (output)
                {
                    // Check for e.Data being null as this can happen when the process is shutdown during dispose.
                    if (e.Data != null)
                    {
                        output.WriteLine(e.Data);
                    }
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
                        Instance.Initialize(uri, process, pidFilePath);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                }

                retries++;
            } while (retries < 30);

            // If we got here, we couldn't launch Selenium or get it to respond. So shut it down.
            ProcessCleanup(process, pidFilePath);
            throw new Exception("Failed to launch the server");
        }

        private static void ProcessCleanup(Process process, string pidFilePath)
        {
            if (process?.HasExited == false)
            {
                try
                {
                    process?.KillTree(TimeSpan.FromSeconds(10));
                    process?.Dispose();
                }
                catch
                {
                }
            }
            try
            {
                if (pidFilePath != null && File.Exists(pidFilePath))
                {
                    File.Delete(pidFilePath);
                }
            }
            catch
            {
            }
        }

        private static async Task<string> WriteTrackingFileAsync(ITestOutputHelper output, string trackingFolder, Process process)
        {
            var pidFile = Path.Combine(trackingFolder, $"{process.Id}.{Guid.NewGuid()}.pid");
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(pidFile, process.Id.ToString());
                    return pidFile;
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

        public void Dispose()
        {
            ProcessCleanup(_process, _sentinelPath);
        }
    }
}
