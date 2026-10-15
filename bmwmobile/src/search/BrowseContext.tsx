import React, { createContext, useCallback, useContext, useMemo, useRef, useState } from 'react';
import { useGarage } from '../location/GarageContext';
import type { ItemTypeFilter, QueryChip } from '../types/listing';
import type { SaleType } from '../types/models';
import type { SearchState } from '../components/SearchBox';
import type { SheetFilters } from '../components/FilterSheet';
import type { SearchInput } from '../hooks/useListingSearch';
import { categoryLabel } from '../utils/categories';

/** Home's second filter: All, Auction or Direct sale (FixedPrice). */
export type SaleFilter = 'All' | SaleType;

type FiltersUpdate = SheetFilters | ((current: SheetFilters) => SheetFilters);

type BrowseValue = {
  /** Step 1 – what: Parts (default) or Vehicles. */
  itemType: ItemTypeFilter;
  setItemType: (value: ItemTypeFilter) => void;
  /** Step 2 – how: All, Auction or Direct sale. */
  saleType: SaleFilter;
  setSaleType: (value: SaleFilter) => void;
  /** The submitted search (text + chips; the category is in `filters`). */
  search: SearchState;
  setSearch: (value: SearchState) => void;
  /** What is typed in the search box right now (the same on Home and Market). */
  draftText: string;
  setDraftText: (text: string) => void;
  /** The chips shown in the search box: the search's chips plus the chosen category. */
  chips: QueryChip[];
  /** A suggestion, a recent search or a category tile: its chips are ADDED (same kind replaced); other filters stay. */
  applyChips: (chips: QueryChip[], text?: string) => void;
  /** Removes one chip from the search box (a category chip clears the category). */
  removeChip: (chip: QueryChip) => void;
  /** Keyboard "search": the typed text with every active chip and filter. */
  submitText: () => void;
  /** Category / part type / make pills and filter-sheet values. */
  filters: SheetFilters;
  setFilters: (update: FiltersUpdate) => void;
  /** ✕ in the search box: clears the text and every chip in the box (also the category); the other filters stay. */
  clearSearch: () => void;
  /** Clears the search and every filter (also My Garage and Auction / Direct sale). Parts / Vehicles stays. */
  clearAll: () => void;
  /** Everything above as one search (useListingSearch). Home and Market both search with exactly this. */
  input: SearchInput;
  /** True when a search or any filter is active (shows "Clear all"). */
  hasAnything: boolean;
};

const EMPTY_SEARCH: SearchState = { text: '', chips: [] };
const PART_ONLY_CHIPS = ['partType', 'partNumber', 'size', 'position', 'category'];
/** A chip and a pill / sheet value for the same thing: whichever was chosen last wins, the other is dropped. */
const CHIPS_REPLACED_BY_FILTER: Partial<Record<keyof SheetFilters, QueryChip['type'][]>> = {
  partType: ['partType'],
  make: ['make', 'model', 'year'],
  model: ['model'],
  year: ['year'],
};
const FILTERS_REPLACED_BY_CHIP: Partial<Record<QueryChip['type'], (keyof SheetFilters)[]>> = {
  partType: ['partType'],
  make: ['make', 'model'],
  model: ['model'],
  year: ['year'],
};

export const categoryChip = (category: string): QueryChip => ({ type: 'category', value: category, label: categoryLabel(category) });

const BrowseContext = createContext<BrowseValue | null>(null);

/**
 * ONE search / filter state for Home and Market: Parts / Vehicles, All / Auction / Direct sale, the search box (text
 * and chips), the category and the filter sheet. Both screens read and change only this, so they can't drift apart:
 * a suggestion, a category tile or a pill on either screen changes the results on both.
 */
export function BrowseProvider({ children }: { children: React.ReactNode }) {
  const { selected: garage, select: selectGarage } = useGarage();
  const [itemType, setItemTypeState] = useState<ItemTypeFilter>('Part');
  const [saleType, setSaleType] = useState<SaleFilter>('All');
  const [search, setSearchState] = useState<SearchState>(EMPTY_SEARCH);
  const [draftText, setDraftText] = useState('');
  const [filters, setFiltersState] = useState<SheetFilters>({});
  // The latest values for the callbacks below (they change several pieces of state at once).
  const latest = useRef({ search, filters, draftText });
  latest.current = { search, filters, draftText };

  const setSearch = useCallback((value: SearchState) => {
    latest.current.search = value;
    setSearchState(value);
    setDraftText(value.text);
  }, []);

  const setFilters = useCallback((update: FiltersUpdate) => {
    const current = latest.current.filters;
    const next = typeof update === 'function' ? update(current) : update;
    const replaced = (Object.keys(CHIPS_REPLACED_BY_FILTER) as (keyof SheetFilters)[])
      .filter(k => next[k] !== undefined && next[k] !== current[k])
      .flatMap(k => CHIPS_REPLACED_BY_FILTER[k]!);
    if (replaced.length) setSearchState(s => ({ ...s, chips: s.chips.filter(c => !replaced.includes(c.type)) }));
    latest.current.filters = next;
    setFiltersState(next);
  }, []);

  const applyChips = useCallback((picked: QueryChip[], text = '') => {
    const category = picked.find(c => c.type === 'category');
    const chips = picked.filter(c => c.type !== 'category');
    const clearFilters = chips.flatMap(c => FILTERS_REPLACED_BY_CHIP[c.type] ?? []);
    if (category || clearFilters.length) {
      setFilters(f => {
        const next = { ...f };
        clearFilters.forEach(k => { next[k] = undefined; });
        if (category && category.value !== f.category) {
          next.category = category.value;
          next.partType = undefined;
        }
        return next;
      });
    }
    const current = latest.current.search;
    const merged = [...current.chips.filter(c => !chips.some(n => n.type === c.type)), ...chips];
    setSearch({ text, chips: merged });
  }, [setFilters, setSearch]);

  const removeChip = useCallback((chip: QueryChip) => {
    if (chip.type === 'category') {
      setFilters(f => ({ ...f, category: undefined, partType: undefined }));
      return;
    }
    const current = latest.current.search;
    const next = { ...current, chips: current.chips.filter(c => !(c.type === chip.type && c.value === chip.value)) };
    latest.current.search = next;
    setSearchState(next);
  }, [setFilters]);

  const submitText = useCallback(() => {
    const { search: current, draftText: text } = latest.current;
    latest.current.search = { ...current, text };
    setSearchState(latest.current.search);
  }, []);

  // Switching between Parts and Vehicles drops what only fits the other one (category, part type, part chips).
  const setItemType = useCallback(
    (value: ItemTypeFilter) => {
      if (value === itemType) return;
      setFilters(f => ({ ...f, category: undefined, partType: undefined, kind: undefined, make: undefined, model: undefined }));
      if (value === 'Vehicle') setSearchState(s => ({ ...s, chips: s.chips.filter(c => !PART_ONLY_CHIPS.includes(c.type)) }));
      setItemTypeState(value);
    },
    [itemType, setFilters],
  );

  const clearSearch = useCallback(() => {
    setSearch(EMPTY_SEARCH);
    setFilters(f => ({ ...f, category: undefined, partType: undefined }));
  }, [setFilters, setSearch]);

  const clearAll = useCallback(() => {
    setSearch(EMPTY_SEARCH);
    setFilters(f => ({ sort: f.sort }));
    setSaleType('All');
    selectGarage(null);
  }, [selectGarage, setFilters, setSearch]);

  const chips = useMemo(
    () => (filters.category ? [categoryChip(filters.category), ...search.chips] : search.chips),
    [filters.category, search.chips],
  );

  const input = useMemo<SearchInput>(
    () => ({
      text: search.text,
      chips: search.chips,
      filters,
      itemType,
      saleType: saleType === 'All' ? undefined : saleType,
    }),
    [search, filters, itemType, saleType],
  );

  const hasAnything =
    !!search.text.trim() || search.chips.length > 0 || saleType !== 'All' || (itemType === 'Part' && !!garage) ||
    Object.entries(filters).some(([k, v]) => k !== 'sort' && v !== undefined);

  const value = useMemo<BrowseValue>(
    () => ({
      itemType, setItemType, saleType, setSaleType, search, setSearch, draftText, setDraftText, chips, applyChips, removeChip,
      submitText, filters, setFilters, clearSearch, clearAll, input, hasAnything,
    }),
    [itemType, setItemType, saleType, search, setSearch, draftText, chips, applyChips, removeChip, submitText, filters, setFilters,
      clearSearch, clearAll, input, hasAnything],
  );
  return <BrowseContext.Provider value={value}>{children}</BrowseContext.Provider>;
}

export function useBrowse(): BrowseValue {
  const ctx = useContext(BrowseContext);
  if (!ctx) throw new Error('useBrowse must be used inside <BrowseProvider>');
  return ctx;
}
