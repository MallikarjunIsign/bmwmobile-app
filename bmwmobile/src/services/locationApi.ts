import { getValidAccessToken } from '../auth/session';
import type { City, CountryCode, MyLocations, SaveLocationRequest } from '../types/location';
import { ApiError, identityJson } from './authApi';

async function token(): Promise<string> {
  const t = await getValidAccessToken();
  if (!t) throw new ApiError('Please log in again.', 401, 'UNAUTHORIZED');
  return t;
}

const q = (params: Record<string, string | number | undefined | null>) =>
  Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== null && v !== '')
    .map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`)
    .join('&');

/** The signed-in user's saved addresses and the city catalog – both on IdentityService. */
export const LocationApi = {
  searchCities: (text: string, country?: CountryCode | null) =>
    identityJson<City[]>('get', `/api/locations/cities?${q({ q: text, country })}`),

  list: async () => identityJson<MyLocations>('get', '/api/me/locations', undefined, await token()),

  add: async (body: SaveLocationRequest) => identityJson<MyLocations>('post', '/api/me/locations', body, await token()),

  update: async (id: string, body: SaveLocationRequest) =>
    identityJson<MyLocations>('put', `/api/me/locations/${id}`, body, await token()),

  remove: async (id: string) => identityJson<MyLocations>('delete', `/api/me/locations/${id}`, undefined, await token()),

  /** Use this address from now on (GPS no longer changes the location). */
  setPrimary: async (id: string) => identityJson<MyLocations>('post', `/api/me/locations/${id}/primary`, undefined, await token()),

  /** "Use my current location": the GPS position becomes the location again and follows the phone, with this radius. */
  setCurrent: async (latitude: number, longitude: number, radiusValue: number) =>
    identityJson<MyLocations>('put', '/api/me/locations/current', { latitude, longitude, radiusValue, explicit: true }, await token()),

  /** Only the radius of a saved location or the GPS row, in its unit (5–1,000). */
  setRadius: async (id: string, radiusValue: number) =>
    identityJson<MyLocations>('put', `/api/me/locations/${id}/radius`, { radiusValue }, await token()),
};
