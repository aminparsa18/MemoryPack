# TypeScriptBench

Compares the TypeScript the generator emits with hand-written decoders, on a real consumer
payload: Flare's `POST /api/logs/search` response (`LogSearchResponse` holding 200 `LogEventDto`s,
each with three `string -> string` attribute dictionaries). It was used to develop
`MemoryPackGenerator_TypeScriptDictionaryAsRecord` (see the root README).

## Variants

| Variant | Code | Runtime | Consumer mapping |
|---|---|---|---|
| `hand` | `ts/handwritten/`: Flare's hand-written decoders (Flare.Net @ df4f9e89, imports rewritten) | stock 1.21.4 runtime, as Flare ships it | `toLogEventDto` |
| `record` | `ts/generated-record/`: this generator, `DictionaryAsRecord=true` | this generator | `toLogEventDto` |
| `map` | `ts/generated-map/`: this generator, default `Map` emission | this generator | `toLogEventDtoFromMaps` (`mapToRecord` per dictionary, as Flare did before ADR-0216) |

`Models.cs` mirrors the C# models. `TypeScriptBench.csproj` generates `generated-record`, and
`map/TypeScriptBench.Map.csproj` compiles the same file into `generated-map`. `fixtures.ts` and
`consumer-mapping.ts` come from Flare's `src/dashboard/bench`.

## Running

```bash
cd ts
npm install
npm run generate     # dotnet build both projects -> generated-record/, generated-map/
npm run fixtures     # fixtures/{one,page}.bin from Flare's seed-42 fixtures; C# checks it re-serializes them
                     # byte-identically and writes page-dirty-padding.bin
npm run roundtrip    # decode -> re-encode -> byte-compare for every variant (exit 1 on failure)
npm run bench -- --rounds=30 --time=1000    # interleaved, one process per variant per round
```

`npm run bench` takes `--variants=hand,record`, `--cases=Decode_Page,DecodeAndMap_Page` and `--json`.
The first variant is the baseline. `pairedPercent` is the median, over rounds, of each variant's
ratio to the baseline run in the same round. That cancels most drift between rounds, which is
larger (5-8% sd) than the differences being measured.

## Results (2026-11-04, Node 26.5.0, macOS, 30 rounds x 1000 ms per case)

ops/s are medians over rounds; paired % is against `hand`, with its interquartile range.

| Case | hand | record | map |
|---|---|---|---|
| Decode, 1 row | 127,386 | 123,607 (−0.5% [−6.9, 4.0]) | 116,605 (−7.7% [−8.7, −4.3]) |
| Decode + map, 1 row | 101,913 | 97,867 (−1.1% [−5.4, 4.9]) | 90,103 (−10.5% [−13.4, −5.0]) |
| Decode, 200 rows | 600 | 592 (−0.9% [−7.5, 2.8]) | 561 (−5.2% [−10.6, −2.7]) |
| Decode + map, 200 rows | 503 | 493 (−0.2% [−8.1, 2.1]) | 446 (−10.2% [−16.0, −8.2]) |

`map` reproduces Flare's ADR-0216 measurement (−10.8% on 200-row decode + map). `record` is level
with the hand-written decoders within noise.

### What closed the gap

Measured with experimental variants on the 200-row cases (30 paired rounds each):

| `string -> string` decode | Decode + map vs hand |
|---|---|
| generic `reader.readRecord(reader => reader.readString() ?? "", reader => reader.readString() ?? "")` | −3.6% / −4.3% (two batches; IQR below 0) |
| same without the `__proto__` guard | −3.2% |
| generic, with the two arrow functions hoisted to module constants | −1.6% (IQR −6.7 to 3.0) |
| dedicated `reader.readStringRecord()` (now emitted) | −0.4% / −0.2% |

The cost was the two arrow functions allocated per `readRecord` call (6 per row). It shows up in
decode + map but not in decode alone. The `__proto__` guard costs nothing measurable.

Other differences between generated and hand-written `deserializeCore`: none in read order, object
construction (`new` on a class, same field initialisation) or versioning branches. The generated
`readDateTimeOffset` skips the 8 offset/padding bytes instead of making two `readInt32` calls, and
`readNullableUint64` reads only the HasValue byte. Both are no slower.

### Correctness

`npm run roundtrip`: all three variants re-encode `one.bin` and `page.bin` byte-identically. On
`page-dirty-padding.bin` (94 null `SpanDurationNano` whose padding bytes are 0xAB, as C# can emit),
`record` and `map` decode all 94 as null. `hand` decodes all 94 as values, because the stock
`readNullableUint64` reads the whole 8-byte flag.
