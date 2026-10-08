export type Check = (value: unknown) => boolean;
export const string: Check = value => typeof value === 'string';
export const boolean: Check = value => typeof value === 'boolean';
export const count: Check = value => typeof value === 'number' && Number.isInteger(value) && value >= 0;
export const percent: Check = value => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100;
export const nullable = (check: Check): Check => value => value === null || check(value);
export const array = (check: Check): Check => value => Array.isArray(value) && value.every(check);
export const object = (shape: Record<string, Check>): Check => value => value !== null && typeof value === 'object'
  && !Array.isArray(value) && Object.entries(shape).every(([key, check]) => check((value as Record<string, unknown>)[key]));
export const timestamp: Check = value => typeof value === 'string' && Number.isFinite(Date.parse(value));
