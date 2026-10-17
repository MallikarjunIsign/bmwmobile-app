export type CountryCode = 'IN' | 'US' | 'NG' | 'IE';

/**
 * The location the app uses: the phone's position, named by the backend (GET /geo/reverse), or the saved
 * address the user chose (then addressId is set). Filters parts by distance and is shown under the name on Home.
 */
export interface AutoLocation {
  latitude: number;
  longitude: number;
  /** "Hyderabad" (null when the backend can't name the place). */
  city: string | null;
  /** Null when the place is outside India, USA, Nigeria and Ireland. */
  countryCode: CountryCode | null;
  /** Search radius (as km); radiusValue / radiusUnit as the user chose it (default 200 mi in the USA, 200 km elsewhere). */
  radiusKm: number;
  radiusValue: number;
  radiusUnit: 'km' | 'mi';
  /** ISO time of the last update. */
  updatedAt: string;
  /** Set when this is a saved address the user chose (not GPS). */
  addressId?: string;
  /** Home / Work / … for a saved address. */
  label?: string;
  /** Area / locality, e.g. "Kukatpally" (saved address, or GPS named by "Search location"). */
  area?: string | null;
}

/** GET /geo/reverse */
export interface ReverseGeocode {
  latitude: number;
  longitude: number;
  city: string | null;
  countryCode: string | null;
  supported: boolean;
  radiusValue: number;
  radiusUnit: 'km' | 'mi';
  radiusKm: number;
}

/** A saved address (IdentityService /api/me/locations). One is primary = the location the app uses. */
export interface SavedLocation {
  id: string;
  /** Home, Work, Other or the user's own name; "Current location" for the GPS row. */
  label: string;
  /** City, e.g. "Hyderabad". */
  placeName: string;
  latitude: number;
  longitude: number;
  radiusKm: number;
  /** The radius in the location's unit (mi in the USA, km elsewhere) – what the user chose. Older servers: missing. */
  radiusValue?: number;
  radiusUnit?: 'km' | 'mi';
  countryCode: CountryCode | null;
  isPrimary: boolean;
  /** The row that follows the phone's GPS ("Use my current location"). */
  isCurrentLocation: boolean;
  addressLine?: string | null;
  area?: string | null;
  landmark?: string | null;
  postalCode?: string | null;
  /** From "Search location" (null on older locations). */
  placeId?: string | null;
  formattedAddress?: string | null;
  city?: string | null;
  state?: string | null;
}

export interface MyLocations {
  autoUpdatePrimary: boolean;
  locations: SavedLocation[];
}

export interface SaveLocationRequest {
  label?: string | null;
  placeName: string;
  latitude: number;
  longitude: number;
  /** The radius in the location's unit (mi in the USA, km elsewhere), 5–1,000. */
  radiusValue?: number;
  countryCode?: string | null;
  makePrimary?: boolean;
  addressLine?: string | null;
  area?: string | null;
  landmark?: string | null;
  postalCode?: string | null;
  placeId?: string | null;
  formattedAddress?: string | null;
  city?: string | null;
  state?: string | null;
}

/** A city from the catalog (GET /api/locations/cities). */
export interface City {
  name: string;
  countryCode: CountryCode;
  latitude: number;
  longitude: number;
}

/** One "Search location" suggestion (GET /places/autocomplete): main text bold, secondary text grey. */
export interface PlaceSuggestion {
  /** A Google place ID, or "city:IN:Hyderabad" from the built-in city list (when Google can't be used). */
  placeId: string;
  primaryText: string;
  secondaryText: string;
}

/** GET /places/autocomplete. source "cities" = Google is not available, these come from the built-in city list. */
export interface PlaceAutocomplete {
  suggestions: PlaceSuggestion[];
  source: 'google' | 'cities';
}

/** A chosen place (GET /places/{placeId}) or the phone's position (GET /places/reverse). */
export interface PlaceDetails {
  placeId: string | null;
  formattedAddress: string;
  lat: number;
  lon: number;
  /** "Kukatpally, Hyderabad" – area + city, else the city. */
  label: string;
  name?: string | null;
  /** "12 Main Street" (street number + road) when the place is an address. */
  street?: string | null;
  area?: string | null;
  city?: string | null;
  state?: string | null;
  stateCode?: string | null;
  countryCode?: string | null;
  postcode?: string | null;
  /** False outside India, USA, Nigeria and Ireland (message: "We don't operate in this country yet"). */
  supported: boolean;
  message?: string | null;
  source: 'google' | 'cache' | 'cities' | 'local';
}
