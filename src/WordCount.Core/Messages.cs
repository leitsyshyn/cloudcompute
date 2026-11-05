namespace WordCount.Core;

/// <summary>Main → worker: count the words that start in [Begin, End) of Path.
/// <c>Stop = true</c> tells the worker to leave its loop.</summary>
public sealed record WorkRequest(bool Stop, int Round, string Path, long Begin, long End);

/// <summary>Worker → main: partial counts for one range plus the worker's own timings.</summary>
public sealed record WorkResult(
    int Round,
    string Worker,
    long BytesRead,
    long Words,
    double ScanMs,
    Dictionary<string, long> Counts);

/// <summary>One measured round, as written to timings.json.</summary>
public sealed record RoundTiming(
    int Round,
    bool Warmup,
    int Points,
    double DispatchAndMapMs,
    double MergeMs,
    double TotalMs,
    long Words,
    int UniqueWords,
    IReadOnlyList<WorkerTiming> Workers);

public sealed record WorkerTiming(string Worker, long Begin, long End, long BytesRead, long Words, int UniqueWords, double ScanMs);

public sealed record RunReport(
    string Method,
    string InputPath,
    long InputBytes,
    int Points,
    double SetupMs,
    double FinalizeMs,
    IReadOnlyList<RoundTiming> Rounds);
