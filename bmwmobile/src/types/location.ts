export type CountryCode = 'IN' | 'US' | 'NG' | 'IE';

export interface City {
  name: string;
  countryCode: CountryCode;
  latitude: number;
  longitude: number;
}

export interface PopularCitiesResponse {
  countryCode: CountryCode;
  countryName: string;
  cities: City[];
}

/** Name for a GPS point: the city, "Near {city}", or "Current location" when far from every known city. */
export interface NearestCity {
  name: string;
  countryCode: CountryCode | null;
  distanceKm: number;
}

/** A saved place (Home, Work, ...) with its own search radius. */
export interface UserLocation {
  id: string;
  label: string;
  placeName: string;
  latitude: number;
  longitude: number;
  radiusKm: number;
  countryCode: CountryCode | null;
  isPrimary: boolean;
  /** The row that follows the phone's GPS while auto-update is on. */
  isCurrentLocation: boolean;
}

export interface MyLocations {
  autoUpdatePrimary: boolean;
  /** Primary first. */
  locations: UserLocation[];
}

export interface SaveLocationRequest {
  label?: string | null;
  placeName: string;
  latitude: number;
  longitude: number;
  radiusKm: number;
  countryCode?: CountryCode | null;
  makePrimary?: boolean;
}

/** A place picked on the location screen (GPS, a popular city or a search result). */
export interface PickedPlace {
  name: string;
  latitude: number;
  longitude: number;
  countryCode: CountryCode | null;
  fromGps: boolean;
}
