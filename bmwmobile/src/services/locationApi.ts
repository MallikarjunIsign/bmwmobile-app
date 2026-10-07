import { getValidAccessToken } from '../auth/session';
import type { City, CountryCode, MyLocations, NearestCity, PopularCitiesResponse, SaveLocationRequest } from '../types/location';
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

/** City catalog (India, USA, Nigeria) and the signed-in user's saved locations — all on IdentityService. */
export const LocationApi = {
  popularCities: (country?: CountryCode | null) =>
    identityJson<PopularCitiesResponse>('get', `/api/locations/popular-cities?${q({ country })}`),

  searchCities: (text: string, country?: CountryCode | null) =>
    identityJson<City[]>('get', `/api/locations/cities?${q({ q: text, country })}`),

  nearestCity: (lat: number, lon: number) =>
    identityJson<NearestCity>('get', `/api/locations/nearest-city?${q({ lat, lon })}`),

  list: async () => identityJson<MyLocations>('get', '/api/me/locations', undefined, await token()),

  add: async (body: SaveLocationRequest) => identityJson<MyLocations>('post', '/api/me/locations', body, await token()),

  update: async (id: string, body: SaveLocationRequest) =>
    identityJson<MyLocations>('put', `/api/me/locations/${id}`, body, await token()),

  remove: async (id: string) => identityJson<MyLocations>('delete', `/api/me/locations/${id}`, undefined, await token()),

  setPrimary: async (id: string) => identityJson<MyLocations>('post', `/api/me/locations/${id}/primary`, undefined, await token()),

  /** Phone GPS → primary. explicit = "Use my current location" (turns auto-update on). */
  updateCurrent: async (latitude: number, longitude: number, opts: { explicit: boolean; radiusKm?: number }) =>
    identityJson<MyLocations>(
      'put',
      '/api/me/locations/current',
      { latitude, longitude, explicit: opts.explicit, radiusKm: opts.radiusKm },
      await token(),
    ),

  setAutoUpdate: async (autoUpdatePrimary: boolean) =>
    identityJson<MyLocations>('put', '/api/me/locations/settings', { autoUpdatePrimary }, await token()),
};
