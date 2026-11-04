// Writes fixtures/one.bin and fixtures/page.bin from Flare's own bench fixtures (seed 42, 200 rows),
// encoded with Flare's hand-written encoder. `dotnet run` (../Program.cs) then checks C# reads and
// re-writes them byte-identically and derives page-dirty-padding.bin.

import { mkdirSync, writeFileSync } from 'node:fs';
import { LogEventDto } from './handwritten/LogEventDto.ts';
import { LogSearchResponse } from './handwritten/LogSearchResponse.ts';
import { buildLogEventDto, buildLogSearchResponsePage, newRandom } from './fixtures.ts';

const PAGE_SIZE = 200;
const dir = new URL('./fixtures/', import.meta.url);
mkdirSync(dir, { recursive: true });

const rand = newRandom(42);
const one = buildLogEventDto(rand);
const page = buildLogSearchResponsePage(rand, PAGE_SIZE);

// `serialize` returns a view of the shared writer's buffer; copy before the next call.
writeFileSync(new URL('one.bin', dir), LogEventDto.serialize(one).slice());
writeFileSync(new URL('page.bin', dir), LogSearchResponse.serialize(page).slice());
console.log('wrote fixtures/one.bin, fixtures/page.bin');
