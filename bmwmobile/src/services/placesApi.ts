import { http, toServerError } from './api';
import { LocationApi } from './locationApi';
import type { City, PlaceAutocomplete, PlaceDetails } from '../types/location';
import { countryName } from '../utils/geo';

/** Suggestions only after this many characters and this pause in typing (keeps the Google bill down). */
export const MIN_PLACE_QUERY = 3;
export const PLACE_DEBOUNCE_MS = 300;

/** One token per search session (typing → picking a result): Google bills the session as one. */
export function newSessionToken(): string {
  // A random UUID v4 (36 characters – Google's limit for session tokens).
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = Math.floor(Math.random() * 16);
    return (c === 'x' ? r : 8 + (r % 4)).toString(16);
  });
}

// Cities from the app-side fallback (the server can't be reached): picked without asking the server again.
const localCities = new Map<string, City>();
const cityId = (c: City) => `city:${c.countryCode}:${c.name}`;

/** A city from the built-in list as a place. */
export const cityPlace = (c: City): PlaceDetails => ({
  placeId: cityId(c), formattedAddress: [c.name, countryName(c.countryCode)].filter(Boolean).join(', '), lat: c.latitude, lon: c.longitude,
  label: c.name, name: c.name, city: c.name, countryCode: c.countryCode, supported: true, source: 'local',
});

/**
 * "Search location" through the Gateway (/places → IdentityService, which calls Google). The app never calls Google.
 * When Google is not available the server answers from the built-in city list; when the server itself can't be
 * reached, the city list is searched directly.
 */
export const PlacesApi = {
  autocomplete: async (
    q: string,
    sessionToken: string,
    near?: { latitude: number; longitude: number } | null,
  ): Promise<PlaceAutocomplete> => {
    try {
      const res = await http.get('/places/autocomplete', {
        params: { q, sessionToken, lat: near?.latitude, lon: near?.longitude },
      });
      return res.data as PlaceAutocomplete;
    } catch (e) {
      const err = toServerError(e, "Couldn't search for places.");
      // Too many searches: the friendly message is shown. Anything else: the built-in city list.
      if (err.status === 429) throw err;
      const cities = await LocationApi.searchCities(q).catch(() => [] as City[]);
      cities.forEach(c => localCities.set(cityId(c), c));
      return {
        source: 'cities',
        suggestions: cities.map(c => ({ placeId: cityId(c), primaryText: c.name, secondaryText: countryName(c.countryCode) ?? '' })),
      };
    }
  },

  /** The chosen suggestion's coordinates and address (closes the session). */
  details: async (placeId: string, sessionToken?: string | null): Promise<PlaceDetails> => {
    const local = localCities.get(placeId);
    if (local) return cityPlace(local);
    try {
      const res = await http.get(`/places/${encodeURIComponent(placeId)}`, { params: { sessionToken: sessionToken ?? undefined } });
      return res.data as PlaceDetails;
    } catch (e) {
      throw toServerError(e, "Couldn't open this place. Please try again.");
    }
  },

  /** "Use my current location": area, city, state, country and postcode for the phone's position. */
  reverse: async (latitude: number, longitude: number): Promise<PlaceDetails> => {
    try {
      const res = await http.get('/places/reverse', { params: { lat: latitude, lon: longitude } });
      return res.data as PlaceDetails;
    } catch (e) {
      throw toServerError(e, "Couldn't name your location.");
    }
  },
};
