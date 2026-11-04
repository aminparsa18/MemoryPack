// One benchmark process for one decoder variant. Run through run-interleaved.ts, which alternates
// variants across processes so JIT state and machine drift are shared fairly.
//
//   tsx bench.ts --variant=hand|record|map [--time=500]
//
// Prints one JSON line: { variant, rows: [{ name, hz, rmePercent, samples }] }.

import { readFileSync } from 'node:fs';
import { Bench } from 'tinybench';
import { loadVariant, variantNames, type VariantName } from './variants.ts';

function arg(name: string): string | undefined {
	return process.argv.find((a) => a.startsWith(`--${name}=`))?.slice(name.length + 3);
}

function load(file: string): ArrayBuffer {
	const bytes = readFileSync(new URL(`./fixtures/${file}`, import.meta.url));
	return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

async function main() {
	const name = arg('variant') as VariantName;
	if (!variantNames.includes(name)) throw new Error(`--variant must be one of ${variantNames.join(', ')}`);
	const time = Number(arg('time') ?? 500);
	const v = await loadVariant(name);

	const one = load('one.bin');
	const page = load('page.bin');

	// Keep results observable so nothing is dead-code eliminated.
	let sink = 0;
	const bench = new Bench({ time });
	bench
		.add('Decode_One', () => {
			sink ^= v.decodeOne(one) == null ? 1 : 0;
		})
		.add('DecodeAndMap_One', () => {
			sink ^= v.mapOne(one).body.length;
		})
		.add('Decode_Page', () => {
			sink ^= v.decodePage(page) == null ? 1 : 0;
		})
		.add('DecodeAndMap_Page', () => {
			sink ^= v.mapPage(page).length;
		});

	const cases = arg('cases')?.split(',');
	if (cases) {
		for (const t of [...bench.tasks]) {
			if (!cases.includes(t.name)) bench.remove(t.name);
		}
	}

	await bench.run();

	const rows = bench.tasks.map((t) => {
		const r = t.result;
		if (r == null || !('throughput' in r)) throw new Error(`${t.name} did not complete`);
		return { name: t.name, hz: r.throughput.mean, rmePercent: r.latency.rme, samples: r.latency.samplesCount };
	});
	console.log(JSON.stringify({ variant: name, sink: sink & 1, rows }));
}

main();
