import type { CountryCode, UserLocation } from '../types/location';

/** Search radius presets (km). Same range as IdentityService (Location:MinRadiusKm..MaxRadiusKm). */
export const RADIUS_OPTIONS_KM = [5, 10, 25, 50, 100, 200] as const;
export const DEFAULT_RADIUS_KM = 50;

/** GPS moves smaller than this don't update the primary location (same as the server). */
export const MOVE_THRESHOLD_KM = 1;

export const LABEL_OPTIONS = ['Home', 'Work', 'Other'] as const;

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

/** Popular-city chips follow the phone number's country (+91 India, +1 USA, +234 Nigeria); India by default. */
export function countryFromPhone(phone?: string | null): CountryCode {
  if (phone?.startsWith('+234')) return 'NG';
  if (phone?.startsWith('+1')) return 'US';
  return 'IN';
}

export const locationIcon = (l: Pick<UserLocation, 'label' | 'isCurrentLocation'>) =>
  l.isCurrentLocation ? '📡' : l.label === 'Home' ? '🏠' : l.label === 'Work' ? '💼' : '📍';

/** "Home · Hyderabad · 25 km" (the label is skipped when it is just the place name). */
export function locationChipLabel(l: Pick<UserLocation, 'label' | 'placeName' | 'radiusKm'>): string {
  const parts = l.label && l.label !== l.placeName ? [l.label, l.placeName] : [l.placeName];
  return `${parts.join(' · ')} · ${l.radiusKm} km`;
}

/** "< 1 km", "8.5 km", "12 km". */
export function formatDistance(km: number): string {
  if (km < 1) return '< 1 km';
  return km < 10 ? `${km.toFixed(1).replace(/\.0$/, '')} km` : `${Math.round(km)} km`;
}

/** "No parts within 25 km of Home – try a bigger radius." */
export const noPartsNearMessage = (l: Pick<UserLocation, 'label' | 'radiusKm'>) =>
  `No parts within ${l.radiusKm} km of ${l.label} – try a bigger radius.`;

/** The line under the radius chips. */
export const radiusSummary = (radiusKm: number, placeName?: string | null) =>
  placeName
    ? `Show parts within ${radiusKm} km of ${placeName}`
    : `Show parts within ${radiusKm} km of the place you pick`;
