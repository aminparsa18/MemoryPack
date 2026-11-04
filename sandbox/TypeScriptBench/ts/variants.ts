// The decoder variants under comparison. Each is a pair of classes with Flare's
// `deserialize`/`serialize` surface plus the consumer mapping that goes with it.

import { toLogEventDto, toLogEventDtoFromMaps, type PlainLogEvent } from './consumer-mapping.ts';

export interface Variant {
	name: string;
	decodeOne(buffer: ArrayBuffer): unknown;
	decodePage(buffer: ArrayBuffer): unknown;
	mapOne(buffer: ArrayBuffer): PlainLogEvent;
	mapPage(buffer: ArrayBuffer): PlainLogEvent[];
	encodeOne(value: unknown): Uint8Array;
	encodePage(value: unknown): Uint8Array;
	spanDurations(page: unknown): (bigint | null)[];
}

export const variantNames = ['hand', 'record', 'map'] as const;
export type VariantName = (typeof variantNames)[number];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Any = any;

export async function loadVariant(name: VariantName): Promise<Variant> {
	// Separate module graphs per variant: only the selected decoders are loaded into the process.
	const mod =
		name === 'hand'
			? await Promise.all([import('./handwritten/LogEventDto.ts'), import('./handwritten/LogSearchResponse.ts')])
			: name === 'record'
				? await Promise.all([import('./generated-record/LogEventDto.ts'), import('./generated-record/LogSearchResponse.ts')])
				: await Promise.all([import('./generated-map/LogEventDto.ts'), import('./generated-map/LogSearchResponse.ts')]);
	const One: Any = mod[0].LogEventDto;
	const Page: Any = mod[1].LogSearchResponse;
	const map: (e: Any) => PlainLogEvent = name === 'map' ? toLogEventDtoFromMaps : toLogEventDto;

	return {
		name,
		decodeOne: (b) => One.deserialize(b),
		decodePage: (b) => Page.deserialize(b),
		mapOne: (b) => map(One.deserialize(b)),
		mapPage: (b) => Page.deserialize(b).events.map((e: Any) => map(e)),
		encodeOne: (v) => One.serialize(v),
		encodePage: (v) => Page.serialize(v),
		spanDurations: (p) => (p as Any).events.map((e: Any) => e.spanDurationNano)
	};
}
