import type { PlaceDetails, SaveLocationRequest } from '../types/location';
import { isCountryCode } from './geo';

/** A picked place as a saved location: label, coordinates and the address parts from "Search location". */
export function toSaveRequest(
  place: PlaceDetails,
  extra: Partial<SaveLocationRequest> & { label?: string | null } = {},
): SaveLocationRequest {
  return {
    placeName: place.label.slice(0, 120),
    latitude: place.lat,
    longitude: place.lon,
    countryCode: isCountryCode(place.countryCode) ? place.countryCode.toUpperCase() : null,
    area: place.area ?? null,
    postalCode: place.postcode ?? null,
    placeId: place.placeId,
    formattedAddress: place.formattedAddress || null,
    city: place.city ?? null,
    state: place.state ?? null,
    ...extra,
  };
}
