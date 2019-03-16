using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Templates.Test.Helpers
{
    public static class Npm
    {
        private static object NpmInstallLock = new object();
        private static SemaphoreSlim NpmSemaphore = new SemaphoreSlim(1);

        internal static async Task<ProcessEx> RestoreWithRetryAsync(ITestOutputHelper output, string workingDirectory)
        {
            // "npm restore" sometimes fails randomly in AppVeyor with errors like:
            //    EPERM: operation not permitted, scandir <path>...
            // This appears to be a general NPM reliability issue on Windows which has
            // been reported many times (e.g., https://github.com/npm/npm/issues/18380)
            // So, allow multiple attempts at the restore.
            const int maxAttempts = 3;
            var attemptNumber = 0;
            ProcessEx restoreResult;
            do
            {
                restoreResult = await RestoreAsync(output, workingDirectory);
                if (restoreResult.ExitCode == 0)
                {
                    return restoreResult;
                }
                else
                {
                    // TODO: We should filter for EPEM here to avoid masking other errors silently.
                    output.WriteLine(
                        $"NPM restore in {workingDirectory} failed on attempt {attemptNumber} of {maxAttempts}. " +
                        $"Error was: {restoreResult.GetFormattedOutput()}");

                    // Clean up the possibly-incomplete node_modules dir before retrying
                    CleanNodeModulesFolder(workingDirectory, output);
                }
                attemptNumber++;
            } while (attemptNumber < maxAttempts);

            output.WriteLine($"Giving up attempting NPM restore in {workingDirectory} after {attemptNumber} attempts.");
            return restoreResult;

            void CleanNodeModulesFolder(string workingDirectory, ITestOutputHelper output)
            {
                var nodeModulesDir = Path.Combine(workingDirectory, "node_modules");
                try
                {
                    if (Directory.Exists(nodeModulesDir))
                    {
                        Directory.Delete(nodeModulesDir, recursive: true);
                    }
                }
                catch
                {
                    output.WriteLine($"Failed to clean up node_modules folder at {nodeModulesDir}.");
                }
            }
        }

        private static async Task<ProcessEx> RestoreAsync(ITestOutputHelper output, string workingDirectory)
        {
            // It's not safe to run multiple NPM installs in parallel
            // https://github.com/npm/npm/issues/2500
            await NpmSemaphore.WaitAsync();
            try
            {
                output.WriteLine($"Restoring NPM packages in '{workingDirectory}' using npm...");
                var result = await ProcessEx.RunViaShellAsync(output, workingDirectory, "npm install");
                return result;
            }
            finally
            {
                NpmSemaphore.Release();
            }
        }
    }
}
