import type { CountryCode } from '../types/location';

/**
 * The automatic location is only updated when the phone moved more than this (or the city changed).
 * Smaller moves keep the current location, so the results don't jump around.
 */
export const MOVE_THRESHOLD_KM = 5;

/** Moves smaller than this are ignored without asking the backend at all. */
export const MIN_MOVE_KM = 1;

export const KM_PER_MILE = 1.609344;

/** Distance unit and currency for each supported country. Distances are km everywhere except the USA. */
export const REGIONS: Record<
  CountryCode,
  { name: string; flag: string; unit: 'km' | 'mi'; currencySymbol: string; currencyCode: string; locale: string }
> = {
  IN: { name: 'India', flag: '🇮🇳', unit: 'km', currencySymbol: '₹', currencyCode: 'INR', locale: 'en-IN' },
  US: { name: 'USA', flag: '🇺🇸', unit: 'mi', currencySymbol: '$', currencyCode: 'USD', locale: 'en-US' },
  NG: { name: 'Nigeria', flag: '🇳🇬', unit: 'km', currencySymbol: '₦', currencyCode: 'NGN', locale: 'en-NG' },
  IE: { name: 'Ireland', flag: '🇮🇪', unit: 'km', currencySymbol: '€', currencyCode: 'EUR', locale: 'en-IE' },
};

export const COUNTRY_CODES = Object.keys(REGIONS) as CountryCode[];

export const DEFAULT_COUNTRY_CODE: CountryCode = 'IN';

export const isCountryCode = (code?: string | null): code is CountryCode =>
  !!code && (COUNTRY_CODES as string[]).includes(code.toUpperCase());

const regionOf = (country?: CountryCode | null) => REGIONS[country ?? DEFAULT_COUNTRY_CODE] ?? REGIONS.IN;

export const distanceUnit = (country?: CountryCode | null) => regionOf(country).unit;

/**
 * Offline fallback only (the backend's GET /geo/reverse decides the radius): 60 miles in the USA, 60 km elsewhere.
 */
export function fallbackRadius(country?: CountryCode | null): { radiusValue: number; radiusUnit: 'km' | 'mi'; radiusKm: number } {
  return distanceUnit(country) === 'mi'
    ? { radiusValue: 60, radiusUnit: 'mi', radiusKm: Math.round(60 * KM_PER_MILE * 100) / 100 }
    : { radiusValue: 60, radiusUnit: 'km', radiusKm: 60 };
}

const EARTH_RADIUS_KM = 6371;
const toRadians = (deg: number) => (deg * Math.PI) / 180;

/** Great-circle distance in km between two points (same formula as the backend GeoMath). */
export function haversineKm(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const dLat = toRadians(lat2 - lat1);
  const dLon = toRadians(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) ** 2 + Math.cos(toRadians(lat1)) * Math.cos(toRadians(lat2)) * Math.sin(dLon / 2) ** 2;
  return EARTH_RADIUS_KM * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

/** Country from the phone number (+91 India, +1 USA, +234 Nigeria, +353 Ireland); India by default. */
export function countryFromPhone(phone?: string | null): CountryCode {
  if (phone?.startsWith('+234')) return 'NG';
  if (phone?.startsWith('+353')) return 'IE';
  if (phone?.startsWith('+1')) return 'US';
  return 'IN';
}

/** A radius in the country's unit: "60 km" or "60 mi". */
export function formatRadius(radiusKm: number, country?: CountryCode | null): string {
  return distanceUnit(country) === 'mi' ? `${Math.round(radiusKm / KM_PER_MILE)} mi` : `${Math.round(radiusKm)} km`;
}

/** "< 1 km", "8.5 km", "12 km" (or the same in miles in the USA). */
export function formatDistance(km: number, country?: CountryCode | null): string {
  const unit = distanceUnit(country);
  const value = unit === 'mi' ? km / KM_PER_MILE : km;
  if (value < 1) return `< 1 ${unit}`;
  return value < 10 ? `${value.toFixed(1).replace(/\.0$/, '')} ${unit}` : `${Math.round(value)} ${unit}`;
}

/** "₹1,50,000", "$1,500", "€1,500", "₦1,500". Amounts are shown as they are (no exchange rates). */
export function formatMoney(amount: number, country?: CountryCode | null, decimals = 0): string {
  const r = regionOf(country);
  let text: string;
  try {
    text = amount.toLocaleString(r.locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
  } catch {
    text = amount.toFixed(decimals);
  }
  return `${r.currencySymbol}${text}`;
}

export const countryName = (country?: CountryCode | null) => (country ? REGIONS[country]?.name ?? null : null);

/** "Kukatpally, Hyderabad, India" – the line under the user's name on Home. */
export function locationText(location: { area?: string | null; city: string | null; countryCode: CountryCode | null } | null): string | null {
  if (!location) return null;
  const parts = [location.area, location.city, countryName(location.countryCode)].filter(Boolean);
  return parts.length ? [...new Set(parts)].join(', ') : null;
}
