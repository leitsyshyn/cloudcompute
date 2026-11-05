// Sequential baseline: the same counting code as the PARCS module, one thread, no PARCS.
//   WordCount.Seq <input> [--repetitions N] [--warmup N] [--output frequencies.txt]
// Prints a RunReport (same shape as the module's timings.json) to stdout.
using System.Diagnostics;
using System.Text.Json;
using WordCount.Core;

if (args.Length == 0 || args[0].StartsWith('-'))
{
    Console.Error.WriteLine("usage: WordCount.Seq <input> [--repetitions N] [--warmup N] [--output file]");
    return 2;
}

var path = Path.GetFullPath(args[0]);
int repetitions = 1, warmup = 0;
string? output = null;
for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--repetitions": repetitions = int.Parse(args[++i]); break;
        case "--warmup": warmup = int.Parse(args[++i]); break;
        case "--output": output = args[++i]; break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}"); return 2;
    }
}

var inputBytes = new FileInfo(path).Length;
var whole = new ByteRange(0, inputBytes);
var rounds = new List<RoundTiming>();
var counts = WordCounting.NewCounts();

for (int round = 0; round < warmup + repetitions; round++)
{
    counts = WordCounting.NewCounts();
    var clock = Stopwatch.StartNew();
    var stats = WordCounting.CountRange(path, whole, counts);
    clock.Stop();
    var ms = clock.Elapsed.TotalMilliseconds;
    rounds.Add(new RoundTiming(round, round < warmup, 1, ms, 0, ms, stats.Words, counts.Count,
        [new WorkerTiming(Environment.MachineName, 0, inputBytes, stats.BytesRead, stats.Words, counts.Count, ms)]));
}

var finalize = Stopwatch.StartNew();
byte[]? frequencies = output is null ? null : WordCounting.FormatFrequencies(counts);
finalize.Stop();
if (output is not null) File.WriteAllBytes(output, frequencies!);

var report = new RunReport("sequential", path, inputBytes, 1, 0, finalize.Elapsed.TotalMilliseconds, rounds);
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return 0;
