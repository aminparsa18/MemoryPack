// The consumer path the benchmark times on top of decode: Flare's `toLogEventDto`
// (src/dashboard/src/lib/api.ts, mirrored verbatim in Flare's bench/consumer-mapping.ts @ df4f9e89).
// `toLogEventDto` is the current one, for decoders that already produce `Record`s (hand-written,
// generated with DictionaryAsRecord). `toLogEventDtoFromMaps` is the ADR-0215 version, which copied
// each generated `Map` into a `Record` with `mapToRecord`.

import { mapToRecord } from './map-record.ts';

export interface PlainLogEvent {
	eventId: string;
	timestamp: string;
	observedTimestamp: string;
	traceId: string;
	spanId: string;
	traceFlags: number;
	severityText: string;
	severityNumber: number;
	serviceName: string;
	body: string;
	resourceSchemaUrl: string;
	resourceAttributes: Record<string, string>;
	scopeSchemaUrl: string;
	scopeName: string;
	scopeVersion: string;
	scopeAttributes: Record<string, string>;
	logAttributes: Record<string, string>;
	eventName: string;
	patternId: string;
	patternTemplate: string;
	spanDurationNano: number | null;
}

interface DecodedLogEvent<A> {
	eventId: string;
	timestamp: Date;
	observedTimestamp: Date;
	traceId: string | null;
	spanId: string | null;
	traceFlags: number;
	severityText: string | null;
	severityNumber: number;
	serviceName: string | null;
	body: string | null;
	resourceSchemaUrl: string | null;
	resourceAttributes: A | null;
	scopeSchemaUrl: string | null;
	scopeName: string | null;
	scopeVersion: string | null;
	scopeAttributes: A | null;
	logAttributes: A | null;
	eventName: string | null;
	patternId: string | null;
	patternTemplate: string | null;
	spanDurationNano: bigint | null;
}

export function toLogEventDto(dto: DecodedLogEvent<Record<string, string>>): PlainLogEvent {
	return {
		eventId: dto.eventId,
		timestamp: dto.timestamp.toISOString(),
		observedTimestamp: dto.observedTimestamp.toISOString(),
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		traceFlags: dto.traceFlags,
		severityText: dto.severityText ?? '',
		severityNumber: dto.severityNumber,
		serviceName: dto.serviceName ?? '',
		body: dto.body ?? '',
		resourceSchemaUrl: dto.resourceSchemaUrl ?? '',
		resourceAttributes: dto.resourceAttributes ?? {},
		scopeSchemaUrl: dto.scopeSchemaUrl ?? '',
		scopeName: dto.scopeName ?? '',
		scopeVersion: dto.scopeVersion ?? '',
		scopeAttributes: dto.scopeAttributes ?? {},
		logAttributes: dto.logAttributes ?? {},
		eventName: dto.eventName ?? '',
		patternId: dto.patternId ?? '',
		patternTemplate: dto.patternTemplate ?? '',
		spanDurationNano: dto.spanDurationNano == null ? null : Number(dto.spanDurationNano)
	};
}

export function toLogEventDtoFromMaps(dto: DecodedLogEvent<Map<string | null, string | null>>): PlainLogEvent {
	return {
		eventId: dto.eventId,
		timestamp: dto.timestamp.toISOString(),
		observedTimestamp: dto.observedTimestamp.toISOString(),
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		traceFlags: dto.traceFlags,
		severityText: dto.severityText ?? '',
		severityNumber: dto.severityNumber,
		serviceName: dto.serviceName ?? '',
		body: dto.body ?? '',
		resourceSchemaUrl: dto.resourceSchemaUrl ?? '',
		resourceAttributes: mapToRecord(dto.resourceAttributes),
		scopeSchemaUrl: dto.scopeSchemaUrl ?? '',
		scopeName: dto.scopeName ?? '',
		scopeVersion: dto.scopeVersion ?? '',
		scopeAttributes: mapToRecord(dto.scopeAttributes),
		logAttributes: mapToRecord(dto.logAttributes),
		eventName: dto.eventName ?? '',
		patternId: dto.patternId ?? '',
		patternTemplate: dto.patternTemplate ?? '',
		spanDurationNano: dto.spanDurationNano == null ? null : Number(dto.spanDurationNano)
	};
}
