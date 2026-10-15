import React, { useState, useEffect, useMemo } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, FlatList } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import type { FacetValue, QueryChip } from '../types/listing';
import { AuctionCard } from '../components/AuctionCard';
import { LocationBanner } from '../components/LocationBanner';
import { NoPartsFound } from '../components/NoPartsFound';
import { FilterSheet, SORTS } from '../components/FilterSheet';
import { BrowseFilters } from '../components/BrowseFilters';
import { useLocations } from '../location/LocationContext';
import { useGarage } from '../location/GarageContext';
import { useListingSearch } from '../hooks/useListingSearch';
import { useBrowse } from '../search/BrowseContext';
import { BrowseSearchBox } from '../search/BrowseSearchBox';
import { formatRadius, distanceUnit, REGIONS } from '../utils/geo';
import { categoryLabel } from '../utils/categories';

export { categoryLabel };

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Marketplace'>,
  NativeStackScreenProps<RootStackParamList>
>;

const Pill = ({ label, count, active, onPress, small }: { label: string; count?: number; active: boolean; onPress: () => void; small?: boolean }) => (
  <TouchableOpacity
    style={[small ? styles.modelPill : styles.pill, active && (small ? styles.modelPillActive : styles.pillActive)]}
    onPress={onPress}
    accessibilityState={{ selected: active }}
  >
    <Text style={[small ? styles.modelText : styles.pillText, active && (small ? styles.modelTextActive : styles.pillTextActive)]}>
      {label}{count !== undefined ? ` (${count})` : ''}
    </Text>
  </TouchableOpacity>
);

/** A selected value stays visible even when the current search has none of it. */
const withSelected = (values: FacetValue[], selected?: string) =>
  selected && !values.some(v => v.value === selected) ? [{ value: selected, count: 0 }, ...values] : values;

/**
 * Market: the search box (suggestions → chips), Home's filters (Parts / Vehicles, All / Auction / Direct sale),
 * pills with counts from the same search (parts: categories → part types; vehicles: makes → models), the filter
 * sheet (with My Garage) and the results. Every request carries every active filter; "Clear all" resets them.
 */
export const MarketplaceSearchScreen: React.FC<Props> = ({ navigation, route }) => {
  const { near, country, location, locationOff } = useLocations();
  const { vehicles: garageVehicles, selected: selectedGarage, select: selectGarage } = useGarage();
  const browse = useBrowse();
  const { itemType, search: submitted, setSearch: setSubmitted, filters, setFilters, clearAll, hasAnything } = browse;
  const [sheet, setSheet] = useState(false);
  const [grid, setGrid] = useState(false);

  // Opened from another screen with a search text / category (older links).
  useEffect(() => {
    const term = route.params?.searchTerm;
    const category = route.params?.category;
    if (term !== undefined) setSubmitted({ text: term, chips: [] });
    if (category !== undefined) setFilters(f => ({ ...f, category: category || undefined }));
  }, [route.params?.searchTerm, route.params?.category, setSubmitted, setFilters]);

  // The same search as Home (one shared state): what Home shows, Market shows.
  const { results, facets, total, loading, error, query, garage } = useListingSearch(browse.input);

  const parts = itemType === 'Part';
  const categories = withSelected(facets.category ?? [], filters.category);
  const partTypes = withSelected(facets.partType ?? [], filters.partType);
  const makes = withSelected(facets.make ?? [], filters.make);
  const models = useMemo(() => {
    const list = (facets.model ?? []).map(v => {
      const [make, model] = v.value.split('|');
      return { make, model, count: v.count };
    });
    return parts ? list : list.filter(m => m.make === filters.make);
  }, [facets.model, parts, filters.make]);

  const createCustomOrder = () => {
    const chip = (t: QueryChip['type']) => submitted.chips.find(c => c.type === t);
    navigation.navigate('OrderPartRequest', {
      searchText: [...submitted.chips.map(c => c.label), submitted.text].filter(Boolean).join(' ') || undefined,
      prefillPart: chip('partType')?.label ?? (submitted.text || (filters.category ? categoryLabel(filters.category) : undefined)),
      prefillMake: query.make,
      prefillModel: query.model,
      prefillYear: query.year,
      prefillPartNumber: chip('partNumber')?.label,
    });
  };

  // Pickup-only listings inside the radius, listings that ship from the whole country.
  const where = near && location ? ` · pickup within ${formatRadius(location.radiusKm, country)}, shipping ${REGIONS[country].name}-wide` : '';
  const noun = parts ? (total === 1 ? 'part' : 'parts') : total === 1 ? 'vehicle' : 'vehicles';
  const sortLabel = SORTS.find(s => s.value === (filters.sort ?? 'best'))?.label ?? 'Best match';
  const activeFilters = [filters.kind, filters.condition, filters.brandType, filters.year, filters.minPrice, filters.maxPrice, filters.maxDistanceKm, filters.delivery, filters.minSellerRating, garage?.id]
    .filter(v => v !== undefined && v !== null).length;

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.searchHeader}>
        <BrowseSearchBox />
        <View style={styles.browse}>
          <BrowseFilters />
        </View>
      </View>

      {locationOff ? (
        <View style={styles.bannerWrap}>
          <LocationBanner />
        </View>
      ) : null}

      {/* Parts: categories → part types. Vehicles: makes → models. Counts from this search. */}
      <View style={styles.pillsWrap}>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} keyboardShouldPersistTaps="handled">
          {parts ? (
            <>
              <Pill label="All categories" active={!filters.category} onPress={() => setFilters(f => ({ ...f, category: undefined, partType: undefined }))} />
              {categories.map(c => (
                <Pill key={c.value} label={categoryLabel(c.value)} count={c.count} active={filters.category === c.value}
                  onPress={() => setFilters(f => ({ ...f, category: f.category === c.value ? undefined : c.value, partType: undefined }))} />
              ))}
            </>
          ) : (
            <>
              <Pill label="All makes" active={!filters.make} onPress={() => setFilters(f => ({ ...f, make: undefined, model: undefined }))} />
              {makes.map(m => (
                <Pill key={m.value} label={m.value} count={m.count} active={filters.make === m.value}
                  onPress={() => setFilters(f => ({ ...f, make: f.make === m.value ? undefined : m.value, model: undefined }))} />
              ))}
            </>
          )}
        </ScrollView>
      </View>

      {(parts && filters.category ? partTypes.length > 0 : models.length > 0) ? (
        <View style={styles.modelsWrap}>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} keyboardShouldPersistTaps="handled">
            {parts && filters.category ? (
              <>
                <Pill small label="All types" active={!filters.partType} onPress={() => setFilters(f => ({ ...f, partType: undefined }))} />
                {partTypes.map(t => (
                  <Pill small key={t.value} label={t.value} count={t.count} active={filters.partType === t.value}
                    onPress={() => setFilters(f => ({ ...f, partType: f.partType === t.value ? undefined : t.value }))} />
                ))}
              </>
            ) : (
              <>
                <Pill small label="All Models" active={!filters.model}
                  onPress={() => setFilters(f => ({ ...f, make: parts ? undefined : f.make, model: undefined }))} />
                {models.map(m => (
                  <Pill small key={`${m.make}|${m.model}`} label={parts ? `${m.make} ${m.model}` : m.model} count={m.count}
                    active={filters.make === m.make && filters.model === m.model}
                    onPress={() => setFilters(f => (f.make === m.make && f.model === m.model
                      ? { ...f, model: undefined, make: parts ? undefined : f.make }
                      : { ...f, make: m.make, model: m.model }))} />
                ))}
              </>
            )}
          </ScrollView>
        </View>
      ) : null}

      {/* Results Header */}
      <View style={styles.resultsHeader}>
        <Text style={styles.resultsCount}>
          {loading ? 'Searching…' : `${total} ${noun} found${where}`}
          {garage ? ` · for your ${garage.year} ${garage.model}` : ''}
        </Text>
        <View style={styles.headerActions}>
          {hasAnything ? (
            <TouchableOpacity onPress={clearAll} accessibilityLabel="Clear all">
              <Text style={styles.clearAll}>Clear all</Text>
            </TouchableOpacity>
          ) : null}
          <TouchableOpacity onPress={() => setGrid(g => !g)} accessibilityLabel="Toggle grid">
            <Text style={styles.sortLabel}>{grid ? '☰' : '▦'}</Text>
          </TouchableOpacity>
          <TouchableOpacity onPress={() => setSheet(true)} accessibilityLabel="Filters">
            <Text style={styles.sortLabel}>⚙ {sortLabel}{activeFilters ? ` · ${activeFilters}` : ''}</Text>
          </TouchableOpacity>
        </View>
      </View>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      <FlatList
        key={grid ? 'grid' : 'list'}
        data={results}
        numColumns={grid ? 2 : 1}
        keyExtractor={item => item.id}
        contentContainerStyle={styles.listContent}
        keyboardShouldPersistTaps="handled"
        renderItem={({ item }) => (
          <AuctionCard
            auction={item}
            compact={grid}
            onPress={() => navigation.navigate('PartDetails', { auctionId: item.id, auction: item })}
            onBidPress={() => navigation.navigate('PlaceBidModal', { auction: item })}
            onSellerPress={() => navigation.navigate('SellerProfile', { seller: item.seller, sellerName: item.sellerName })}
          />
        )}
        ListEmptyComponent={loading || error ? undefined : <NoPartsFound onCreateOrder={createCustomOrder} />}
      />

      <FilterSheet
        visible={sheet}
        onClose={() => setSheet(false)}
        value={filters}
        facets={facets}
        onApply={setFilters}
        unit={distanceUnit(country)}
        currencySymbol={REGIONS[country].currencySymbol}
        hasLocation={!!near}
        garage={parts ? { vehicles: garageVehicles, selectedId: selectedGarage?.id ?? null, onSelect: selectGarage, onManage: () => navigation.navigate('Garage') } : undefined}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  searchHeader: {
    padding: SPACING.md, backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border, zIndex: 10,
  },
  browse: { marginTop: SPACING.sm },
  bannerWrap: { paddingHorizontal: SPACING.md, paddingTop: SPACING.sm, backgroundColor: COLORS.surface },
  pillsWrap: { backgroundColor: COLORS.surface, paddingVertical: SPACING.xs + 2, paddingHorizontal: SPACING.md },
  pill: {
    backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs, borderRadius: RADIUS.full,
    marginRight: SPACING.xs, borderWidth: 1, borderColor: COLORS.border,
  },
  pillActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  pillText: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '600' },
  pillTextActive: { color: COLORS.text, fontWeight: '700' },
  modelsWrap: {
    backgroundColor: COLORS.background, paddingVertical: SPACING.xs + 2, paddingHorizontal: SPACING.md,
    borderBottomWidth: 1, borderBottomColor: COLORS.border,
  },
  modelPill: { backgroundColor: COLORS.cardSecondary, paddingHorizontal: SPACING.sm + 4, paddingVertical: 4, borderRadius: RADIUS.sm, marginRight: SPACING.xs },
  modelPillActive: { backgroundColor: COLORS.badgeBg, borderWidth: 1, borderColor: COLORS.primary },
  modelText: { color: COLORS.textMuted, fontSize: 11, fontWeight: '600' },
  modelTextActive: { color: COLORS.primaryGlow, fontWeight: '700' },
  resultsHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingHorizontal: SPACING.md, paddingVertical: SPACING.sm },
  resultsCount: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '700', flexShrink: 1 },
  headerActions: { flexDirection: 'row', gap: SPACING.md, alignItems: 'center' },
  clearAll: { color: COLORS.danger, fontSize: 12, fontWeight: '700' },
  sortLabel: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '700' },
  error: { color: COLORS.danger, paddingHorizontal: SPACING.md, fontSize: 13 },
  listContent: { padding: SPACING.md, paddingBottom: SPACING.xxl },
});
