using System.Runtime.CompilerServices;
using MemoryPack;
using TypeScriptBench;

// Checks the TypeScript-written fixtures against C# and derives the padding fixture.
//
//   dotnet run -- <fixtures dir>
//
// 1. page.bin / one.bin (written by ts/make-fixtures.ts with the hand-written encoder) must
//    deserialize in C# and re-serialize to the same bytes, so the benchmark input is exactly what
//    the server would send.
// 2. page-dirty-padding.bin: page.bin with every null SpanDurationNano carrying non-zero bytes after
//    the HasValue flag. C# blits Nullable<T> as-is and does not clear that padding; a reader that
//    looks at more than the HasValue byte decodes those nulls as values.

var dir = args.Length > 0 ? args[0] : Path.Combine("ts", "fixtures");

var page = CheckRoundTrip<LogSearchResponse>(Path.Combine(dir, "page.bin"));
CheckRoundTrip<LogEventDto>(Path.Combine(dir, "one.bin"));

var dirtyNull = DirtyNull();
var nulls = 0;
var dirty = page with
{
    Events = page.Events.Select(e =>
    {
        if (e.SpanDurationNano != null) return e;
        nulls++;
        return e with { SpanDurationNano = dirtyNull };
    }).ToArray()
};
var dirtyBytes = MemoryPackSerializer.Serialize(dirty);
var cleanBytes = File.ReadAllBytes(Path.Combine(dir, "page.bin"));
if (dirtyBytes.Length != cleanBytes.Length || dirtyBytes.AsSpan().SequenceEqual(cleanBytes))
{
    throw new Exception("dirty fixture should differ from page.bin only in padding bytes");
}
File.WriteAllBytes(Path.Combine(dir, "page-dirty-padding.bin"), dirtyBytes);
Console.WriteLine($"page-dirty-padding.bin: {nulls} null SpanDurationNano with padding 0xAB");

static T CheckRoundTrip<T>(string path)
{
    var bytes = File.ReadAllBytes(path);
    var value = MemoryPackSerializer.Deserialize<T>(bytes)!;
    var again = MemoryPackSerializer.Serialize(value);
    if (!again.AsSpan().SequenceEqual(bytes))
    {
        throw new Exception($"{path}: C# re-serialization differs from the TypeScript-written bytes");
    }
    Console.WriteLine($"{Path.GetFileName(path)}: {bytes.Length} B, C# round trip byte-identical");
    return value;
}

static ulong? DirtyNull()
{
    ulong? value = null;
    ref var b = ref Unsafe.As<ulong?, byte>(ref value);
    for (var i = 1; i < Unsafe.SizeOf<ulong?>(); i++)
    {
        Unsafe.Add(ref b, i) = 0xAB;
    }
    if (value.HasValue) throw new Exception("HasValue byte was overwritten");
    return value;
}
