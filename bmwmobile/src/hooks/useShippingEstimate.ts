import { useEffect, useState } from 'react';
import { MarketApi } from '../services/marketApi';
import { useLocations } from '../location/LocationContext';
import type { ShippingQuote } from '../types/listing';

/**
 * The shipping estimate for a listing to the buyer's selected location (saved address or GPS). Null while loading,
 * without a location, or when the listing doesn't ship. place = "Kukatpally" for "Shipping to Kukatpally: ~₹350".
 */
export function useShippingEstimate(listingId: string | undefined, enabled: boolean) {
  const { location } = useLocations();
  const [quote, setQuote] = useState<ShippingQuote | null>(null);
  const lat = location?.latitude;
  const lon = location?.longitude;
  const country = location?.countryCode ?? null;

  useEffect(() => {
    setQuote(null);
    if (!enabled || !listingId || lat == null || lon == null) return;
    let alive = true;
    MarketApi.shippingEstimate(listingId, lat, lon, country)
      .then(e => alive && setQuote(e.shipping))
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [listingId, enabled, lat, lon, country]);

  return { quote, place: location ? location.area || location.city || null : null, hasLocation: lat != null };
}
