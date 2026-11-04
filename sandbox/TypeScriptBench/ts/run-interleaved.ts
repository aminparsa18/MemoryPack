// Runs bench.ts in separate processes, rotating the variant order each round, and summarises
// ops/s per case and variant (median, mean, min-max across rounds).
//
//   tsx run-interleaved.ts [--rounds=10] [--time=500] [--variants=hand,record,map] [--cases=a,b] [--json]
//
// `pairedPercent` is the median over rounds of (variant / first variant - 1) within the same round,
// which cancels most machine drift between rounds; [p25, p75] is its interquartile range.

import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

function arg(name: string): string | undefined {
	return process.argv.find((a) => a.startsWith(`--${name}=`))?.slice(name.length + 3);
}

const rounds = Number(arg('rounds') ?? 10);
const time = arg('time') ?? '500';
const variants = (arg('variants') ?? 'hand,record,map').split(',');
const cases = arg('cases');
const benchPath = fileURLToPath(new URL('./bench.ts', import.meta.url));
const tsx = fileURLToPath(new URL('./node_modules/.bin/tsx', import.meta.url));

type Row = { name: string; hz: number };
const results = new Map<string, Map<string, number[]>>(); // case -> variant -> hz per round

for (let r = 0; r < rounds; r++) {
	const order = variants.map((_, i) => variants[(i + r) % variants.length]);
	for (const v of order) {
		const out = execFileSync(tsx, [benchPath, `--variant=${v}`, `--time=${time}`, ...(cases ? [`--cases=${cases}`] : [])], { encoding: 'utf8' });
		const { rows } = JSON.parse(out.trim().split('\n').pop()!) as { rows: Row[] };
		for (const row of rows) {
			if (!results.has(row.name)) results.set(row.name, new Map());
			const byVariant = results.get(row.name)!;
			if (!byVariant.has(v)) byVariant.set(v, []);
			byVariant.get(v)!.push(row.hz);
		}
	}
	process.stderr.write(`round ${r + 1}/${rounds} (${order.join(',')})\n`);
}

const median = (xs: number[]) => {
	const s = [...xs].sort((a, b) => a - b);
	const m = s.length >> 1;
	return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
};
const mean = (xs: number[]) => xs.reduce((a, b) => a + b, 0) / xs.length;
const sd = (xs: number[]) => {
	const m = mean(xs);
	return Math.sqrt(xs.reduce((a, b) => a + (b - m) ** 2, 0) / Math.max(1, xs.length - 1));
};

const quantile = (xs: number[], q: number) => {
	const s = [...xs].sort((a, b) => a - b);
	const pos = (s.length - 1) * q;
	const lo = Math.floor(pos);
	return s[lo] + (s[Math.min(lo + 1, s.length - 1)] - s[lo]) * (pos - lo);
};
const pct = (x: number) => +((x - 1) * 100).toFixed(1);

const summary = [...results].flatMap(([name, byVariant]) => {
	const baseRounds = byVariant.get(variants[0])!;
	const base = median(baseRounds);
	return variants.map((v) => {
		const xs = byVariant.get(v)!;
		const ratios = xs.map((x, i) => x / baseRounds[i]);
		return {
			case: name,
			variant: v,
			medianHz: Math.round(median(xs)),
			meanHz: Math.round(mean(xs)),
			sdPercent: +((sd(xs) / mean(xs)) * 100).toFixed(1),
			minHz: Math.round(Math.min(...xs)),
			maxHz: Math.round(Math.max(...xs)),
			vsFirstPercent: +((median(xs) / base - 1) * 100).toFixed(1),
			pairedPercent: pct(median(ratios)),
			pairedIqr: `[${pct(quantile(ratios, 0.25))}, ${pct(quantile(ratios, 0.75))}]`
		};
	});
});

if (process.argv.includes('--json')) {
	console.log(JSON.stringify({ node: process.version, rounds, time: Number(time), variants, summary, raw: Object.fromEntries([...results].map(([k, m]) => [k, Object.fromEntries(m)])) }));
} else {
	console.log(`node ${process.version}, ${rounds} rounds x ${time} ms per case, baseline = ${variants[0]}`);
	console.table(summary);
}
