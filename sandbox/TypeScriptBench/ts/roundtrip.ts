// Correctness check for every variant: decode the C#-verified fixtures, re-encode, byte-compare.
// For page-dirty-padding.bin (null SpanDurationNano with non-zero padding), the decoded page must
// equal page.bin's: same null count, and re-encoding must give page.bin's bytes exactly.
//
//   tsx roundtrip.ts        (exit code 1 if any variant fails)

import { readFileSync } from 'node:fs';
import { loadVariant, variantNames } from './variants.ts';

function load(file: string): ArrayBuffer {
	const bytes = readFileSync(new URL(`./fixtures/${file}`, import.meta.url));
	return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

const equal = (a: Uint8Array, b: ArrayBuffer) => {
	const bb = new Uint8Array(b);
	return a.length === bb.length && a.every((x, i) => x === bb[i]);
};

let failed = false;
for (const name of variantNames) {
	const v = await loadVariant(name);
	const one = load('one.bin');
	const page = load('page.bin');
	const dirty = load('page-dirty-padding.bin');

	const oneOk = equal(v.encodeOne(v.decodeOne(one)), one);
	const pageDecoded = v.decodePage(page);
	const pageOk = equal(v.encodePage(pageDecoded), page);
	const dirtyDecoded = v.decodePage(dirty);
	const nulls = v.spanDurations(pageDecoded).filter((x) => x == null).length;
	const dirtyNulls = v.spanDurations(dirtyDecoded).filter((x) => x == null).length;
	const dirtyOk = equal(v.encodePage(dirtyDecoded), page);

	const ok = oneOk && pageOk && dirtyOk && nulls === dirtyNulls;
	failed ||= !ok;
	console.log(
		`${name.padEnd(6)} one: ${oneOk ? 'ok' : 'DIFF'}  page: ${pageOk ? 'ok' : 'DIFF'}  dirty padding: ${dirtyNulls}/${nulls} nulls kept, re-encode ${dirtyOk ? 'ok' : 'DIFF'}`
	);
}
process.exit(failed ? 1 : 0);
