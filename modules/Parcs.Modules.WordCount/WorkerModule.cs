using System.Diagnostics;
using Parcs.Net;
using WordCount.Core;

namespace Parcs.Modules.WordCount;

/// <summary>Runs on a daemon point. Serves requests from the main module until told to stop:
/// each request names a byte range of a file on the shared storage; the worker reads only that
/// range itself (no file data travels over the channel) and sends back its partial counts.</summary>
public sealed class WorkerModule : IModule
{
    public async Task RunAsync(IModuleInfo moduleInfo, CancellationToken cancellationToken = default)
    {
        var worker = Environment.MachineName;

        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await moduleInfo.Parent.ReadObjectAsync<WorkRequest>();
            if (request.Stop) break;

            var counts = WordCounting.NewCounts();
            var stopwatch = Stopwatch.StartNew();
            var stats = WordCounting.CountRange(request.Path, new ByteRange(request.Begin, request.End), counts);
            stopwatch.Stop();

            await moduleInfo.Parent.WriteObjectAsync(new WorkResult(
                request.Round, worker, stats.BytesRead, stats.Words, stopwatch.Elapsed.TotalMilliseconds, counts));
        }
    }
}
