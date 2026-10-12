import { useEffect, useMemo, useState } from 'react';
import { MarketApi } from '../services/marketApi';
import { mapRawDtoToAuction } from '../services/api';
import { useLocations } from '../location/LocationContext';
import { useGarage } from '../location/GarageContext';
import type { Auction } from '../types/models';
import type { FacetValue, GarageVehicle, ListingSearchQuery, QueryChip } from '../types/listing';
import type { SheetFilters } from '../components/FilterSheet';

export interface SearchInput {
  text: string;
  chips: QueryChip[];
  filters: SheetFilters;
}

/**
 * Chips + text + filter sheet + the selected garage vehicle → the search API query.
 * Part number / size / position chips go back into the text (the server understands them there).
 */
export function buildQuery(input: SearchInput, garage: GarageVehicle | null): ListingSearchQuery {
  const chip = (type: QueryChip['type']) => input.chips.find(c => c.type === type);
  const textParts = [
    chip('partNumber')?.label,
    chip('size')?.label,
    chip('partType') ? null : chip('position')?.value,
    input.text.trim(),
  ].filter(Boolean);
  const partTypeChip = chip('partType');
  // "Front bumper" chip → part type Bumper + the position word in the text.
  const positionWord = partTypeChip && partTypeChip.label.toLowerCase() !== partTypeChip.value.toLowerCase()
    ? partTypeChip.label.toLowerCase().split(' ').filter(w => ['front', 'rear', 'left', 'right'].includes(w)).join(' ')
    : '';
  if (positionWord) textParts.unshift(positionWord);

  const make = input.filters.make ?? chip('make')?.value ?? garage?.make;
  const fromGarage = !input.filters.make && !chip('make') && garage;
  const yearChip = chip('year')?.value;

  return {
    q: textParts.join(' ') || undefined,
    partType: input.filters.partType ?? partTypeChip?.value,
    make,
    model: input.filters.model ?? chip('model')?.value ?? (fromGarage ? garage!.model : undefined),
    year: input.filters.year ?? (yearChip ? parseInt(yearChip, 10) : undefined) ?? (fromGarage ? garage!.year : undefined),
    category: input.filters.category ?? chip('category')?.value,
    kind: input.filters.kind,
    condition: input.filters.condition,
    brandType: input.filters.brandType,
    minPrice: input.filters.minPrice,
    maxPrice: input.filters.maxPrice,
    maxDistanceKm: input.filters.maxDistanceKm,
    delivery: input.filters.delivery,
    minSellerRating: input.filters.minSellerRating,
    sort: input.filters.sort ?? 'best',
  };
}

/** Live listings for the search (within 60 km / 60 mi, or country-wide while location is off). */
export function useListingSearch(input: SearchInput, enabled = true) {
  const { near, country } = useLocations();
  const { selected: garage } = useGarage();
  const query = useMemo(() => buildQuery(input, garage), [input, garage]);
  const [results, setResults] = useState<Auction[]>([]);
  const [facets, setFacets] = useState<Record<string, FacetValue[]>>({});
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    setLoading(true);
    setError(null);
    MarketApi.search(query, near, country)
      .then(r => {
        if (!alive) return;
        setResults(r.results.map(hit => ({ ...mapRawDtoToAuction(hit), currentHighBid: hit.currentPrice, bidCount: hit.bidCount })));
        setFacets(r.facets ?? {});
        setTotal(r.totalCount);
      })
      .catch(e => alive && setError(e?.message ?? "Couldn't search right now"))
      .finally(() => alive && setLoading(false));
    return () => {
      alive = false;
    };
  }, [query, near, country, enabled, reloadKey]);

  return { query, results, facets, total, loading, error, reload: () => setReloadKey(k => k + 1), garage };
}
