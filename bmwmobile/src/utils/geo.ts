import type { CountryCode, UserLocation } from '../types/location';

/** Search radius presets (km). Same range as IdentityService (Location:MinRadiusKm..MaxRadiusKm). */
export const RADIUS_OPTIONS_KM = [5, 10, 25, 50, 100, 200] as const;
export const DEFAULT_RADIUS_KM = 50;

/** USA presets are 5, 10, 25, 50 and 100 miles, saved as whole km (the server stores km). */
export const RADIUS_OPTIONS_MI = [5, 10, 25, 50, 100] as const;

/** GPS moves smaller than this don't update the primary location (same as the server). */
export const MOVE_THRESHOLD_KM = 1;

export const LABEL_OPTIONS = ['Home', 'Work', 'Other'] as const;

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

const regionOf = (country?: CountryCode | null) => REGIONS[country ?? DEFAULT_COUNTRY_CODE] ?? REGIONS.IN;

export const distanceUnit = (country?: CountryCode | null) => regionOf(country).unit;

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

/** Popular-city chips follow the phone number's country (+91 India, +1 USA, +234 Nigeria, +353 Ireland); India by default. */
export function countryFromPhone(phone?: string | null): CountryCode {
  if (phone?.startsWith('+234')) return 'NG';
  if (phone?.startsWith('+353')) return 'IE';
  if (phone?.startsWith('+1')) return 'US';
  return 'IN';
}

/** Radius chips for a country, as km values to save: 5–200 km, or 5–100 miles in the USA. */
export function radiusOptionsKm(country?: CountryCode | null): number[] {
  return distanceUnit(country) === 'mi' ? RADIUS_OPTIONS_MI.map(mi => Math.round(mi * KM_PER_MILE)) : [...RADIUS_OPTIONS_KM];
}

/** The radius chip closest to a saved radius (used when the country, and so the unit, changes). */
export function nearestRadiusKm(radiusKm: number, country?: CountryCode | null): number {
  const options = radiusOptionsKm(country);
  return options.reduce((best, km) => (Math.abs(km - radiusKm) < Math.abs(best - radiusKm) ? km : best), options[0]);
}

/** A saved radius in the country's unit: "25 km" or "25 mi". */
export function formatRadius(radiusKm: number, country?: CountryCode | null): string {
  return distanceUnit(country) === 'mi' ? `${Math.round(radiusKm / KM_PER_MILE)} mi` : `${radiusKm} km`;
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

export const locationIcon = (l: Pick<UserLocation, 'label' | 'isCurrentLocation'>) =>
  l.isCurrentLocation ? '📡' : l.label === 'Home' ? '🏠' : l.label === 'Work' ? '💼' : '📍';

/** "Home · Hyderabad · 25 km" (the label is skipped when it is just the place name). */
export function locationChipLabel(l: Pick<UserLocation, 'label' | 'placeName' | 'radiusKm' | 'countryCode'>): string {
  const parts = l.label && l.label !== l.placeName ? [l.label, l.placeName] : [l.placeName];
  return `${parts.join(' · ')} · ${formatRadius(l.radiusKm, l.countryCode)}`;
}

/** "No parts within 25 km of Home – try a bigger radius." */
export const noPartsNearMessage = (l: Pick<UserLocation, 'label' | 'radiusKm' | 'countryCode'>) =>
  `No parts within ${formatRadius(l.radiusKm, l.countryCode)} of ${l.label} – try a bigger radius.`;

/** The line under the radius chips. */
export const radiusSummary = (radiusKm: number, placeName?: string | null, country?: CountryCode | null) =>
  placeName
    ? `Show parts within ${formatRadius(radiusKm, country)} of ${placeName}`
    : `Show parts within ${formatRadius(radiusKm, country)} of the place you pick`;
