using Parcs.Net;

namespace Parcs.Modules.WordCount;

/// <summary>Job arguments (strings in the run request, bound by PARCS).</summary>
public sealed class WordCountOptions : IModuleOptions
{
    /// <summary>Number of worker points (one range of the file each).</summary>
    public int PointsNumber { get; set; } = 1;

    /// <summary>Input file. Empty: the first input file uploaded with the job. Relative paths
    /// resolve against the shared storage root, e.g. "datasets/natural_100mb.txt".</summary>
    public string InputPath { get; set; } = "";

    /// <summary>Measured rounds over the same, already started points.</summary>
    public int Repetitions { get; set; } = 1;

    /// <summary>Unmeasured rounds before the measured ones (JIT, page cache).</summary>
    public int Warmup { get; set; } = 0;

    /// <summary>Write frequencies.txt (the final word-count table) to the job output.</summary>
    public bool WriteFrequencies { get; set; } = true;
}
