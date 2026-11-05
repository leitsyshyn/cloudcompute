using System.Diagnostics;
using System.Text.Json;
using Parcs.Net;
using WordCount.Core;

namespace Parcs.Modules.WordCount;

/// <summary>
/// Parallel word-frequency count (lab variant 4.1) on PARCS.
///
/// 1. Setup: create <c>PointsNumber</c> points, open a channel to each and start a
///    <see cref="WorkerModule"/> on it. Measured separately, it is not part of a round.
/// 2. Each round: split the file into PointsNumber contiguous byte ranges, send one range to each
///    point, wait for all partial counts, merge them.
/// 3. Write timings.json (every round, with per-worker scan times) and frequencies.txt.
///
/// Rounds reuse the started points, so repetitions measure the algorithm and not the cost of
/// starting the points.
/// </summary>
public sealed class MainModule : IModule
{
    private const string StorageRoot = "/var/lib/storage";

    public async Task RunAsync(IModuleInfo moduleInfo, CancellationToken cancellationToken = default)
    {
        var options = moduleInfo.BindModuleOptions<WordCountOptions>();
        var pointsCount = Math.Max(1, options.PointsNumber);
        var path = ResolveInputPath(moduleInfo, options.InputPath);
        var inputBytes = new FileInfo(path).Length;

        var setup = Stopwatch.StartNew();
        var points = new IPoint[pointsCount];
        var channels = new IChannel[pointsCount];
        for (int i = 0; i < pointsCount; i++)
        {
            points[i] = await moduleInfo.CreatePointAsync();
            channels[i] = await points[i].CreateChannelAsync();
            await points[i].ExecuteClassAsync<WorkerModule>();
        }
        setup.Stop();

        var ranges = WordCounting.EvenRanges(inputBytes, pointsCount);
        var rounds = new List<RoundTiming>();
        Dictionary<string, long> total = WordCounting.NewCounts();
        int roundsCount = Math.Max(0, options.Warmup) + Math.Max(1, options.Repetitions);

        for (int round = 0; round < roundsCount; round++)
        {
            bool warmup = round < options.Warmup;
            var roundClock = Stopwatch.StartNew();

            for (int i = 0; i < pointsCount; i++)
            {
                await channels[i].WriteObjectAsync(new WorkRequest(false, round, path, ranges[i].Begin, ranges[i].End));
            }
            var results = await Task.WhenAll(channels.Select(c => c.ReadObjectAsync<WorkResult>()));
            var mapMs = roundClock.Elapsed.TotalMilliseconds;

            total = WordCounting.NewCounts();
            foreach (var result in results)
            {
                WordCounting.MergeInto(total, result.Counts);
            }
            roundClock.Stop();

            rounds.Add(new RoundTiming(
                round, warmup, pointsCount,
                DispatchAndMapMs: mapMs,
                MergeMs: roundClock.Elapsed.TotalMilliseconds - mapMs,
                TotalMs: roundClock.Elapsed.TotalMilliseconds,
                Words: results.Sum(r => r.Words),
                UniqueWords: total.Count,
                Workers: results.Select((r, i) => new WorkerTiming(
                    r.Worker, ranges[i].Begin, ranges[i].End, r.BytesRead, r.Words, r.Counts.Count, r.ScanMs)).ToList()));
        }

        for (int i = 0; i < pointsCount; i++)
        {
            await channels[i].WriteObjectAsync(new WorkRequest(true, -1, "", 0, 0));
        }

        var finalize = Stopwatch.StartNew();
        byte[]? frequencies = options.WriteFrequencies ? WordCounting.FormatFrequencies(total) : null;
        finalize.Stop();

        var report = new RunReport("parcs", path, inputBytes, pointsCount,
            setup.Elapsed.TotalMilliseconds, finalize.Elapsed.TotalMilliseconds, rounds);
        await moduleInfo.OutputWriter.WriteToFileAsync(
            JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }), "timings.json");
        if (frequencies is not null)
        {
            await moduleInfo.OutputWriter.WriteToFileAsync(frequencies, "frequencies.txt");
        }

        foreach (var point in points)
        {
            await point.DeleteAsync();
        }
    }

    private static string ResolveInputPath(IModuleInfo moduleInfo, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            var first = moduleInfo.InputReader.GetFilenames().FirstOrDefault()
                ?? throw new ArgumentException("No InputPath argument and no input file uploaded with the job.");
            using var stream = moduleInfo.InputReader.GetFileStreamForFile(first);
            return stream.Name;
        }
        var path = Path.IsPathRooted(inputPath) ? inputPath : Path.Combine(StorageRoot, inputPath);
        return File.Exists(path) ? path : throw new FileNotFoundException($"Input file {path} not found.", path);
    }
}
