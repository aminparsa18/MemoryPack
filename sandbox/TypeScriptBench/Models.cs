using MemoryPack;

namespace TypeScriptBench;

// Field-for-field mirror of Flare.Api.Model.LogEventDto / LogSearchResponse (Flare.Net @ df4f9e89),
// the response shape of Flare's POST /api/logs/search. Member order is the wire order.

[MemoryPackable]
[GenerateTypeScript]
public sealed partial record LogEventDto
{
    public required Guid EventId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required DateTimeOffset ObservedTimestamp { get; init; }
    public required DateTimeOffset IngestedAt { get; init; }
    public required string TraceId { get; init; }
    public required string SpanId { get; init; }
    public required byte TraceFlags { get; init; }
    public required string SeverityText { get; init; }
    public required byte SeverityNumber { get; init; }
    public required string ServiceName { get; init; }
    public required string Body { get; init; }
    public required string ResourceSchemaUrl { get; init; }
    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }
    public required string ScopeSchemaUrl { get; init; }
    public required string ScopeName { get; init; }
    public required string ScopeVersion { get; init; }
    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }
    public required IReadOnlyDictionary<string, string> LogAttributes { get; init; }
    public required string EventName { get; init; }
    public required string PatternId { get; init; }
    public required string PatternTemplate { get; init; }
    public ulong? SpanDurationNano { get; init; }
    public uint DroppedAttributesCount { get; init; }
}

[MemoryPackable]
[GenerateTypeScript]
public sealed partial record LogSearchResponse
{
    public required IReadOnlyList<LogEventDto> Events { get; init; }
    public string? NextCursor { get; init; }
}
