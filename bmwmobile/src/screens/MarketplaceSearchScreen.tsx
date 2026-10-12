import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, FlatList } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService } from '../services/api';
import { FilterOptions } from '../types/models';
import type { QueryChip } from '../types/listing';
import { AuctionCard } from '../components/AuctionCard';
import { LocationBanner } from '../components/LocationBanner';
import { NoPartsFound } from '../components/NoPartsFound';
import { SearchBox, rememberSearch, type SearchState } from '../components/SearchBox';
import { FilterSheet, SORTS, type SheetFilters } from '../components/FilterSheet';
import { useLocations } from '../location/LocationContext';
import { garageLabel, useGarage } from '../location/GarageContext';
import { useListingSearch } from '../hooks/useListingSearch';
import { formatRadius, distanceUnit, REGIONS } from '../utils/geo';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Marketplace'>,
  NativeStackScreenProps<RootStackParamList>
>;

/** "WheelsAndTyres" → "Wheels & Tyres". */
export const categoryLabel = (value: string) =>
  value.replace(/And/g, ' & ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/\s+/g, ' ').trim();

type ModelPill = { make: string; model: string };
const modelKey = (m: ModelPill) => `${m.make}|${m.model}`;

/**
 * Marketplace: one search box with suggestions and chips, the predefined pills (categories and make/model from the
 * live listings near the user), a filter sheet, sort, and results like an online shop.
 */
export const MarketplaceSearchScreen: React.FC<Props> = ({ navigation, route }) => {
  const { near, country, location, locationOff } = useLocations();
  const { vehicles: garageVehicles, selected: garage, select: selectGarage } = useGarage();
  const [search, setSearch] = useState<SearchState>({ text: route.params?.searchTerm ?? '', chips: [] });
  const [submitted, setSubmitted] = useState<SearchState>(search);
  const [filters, setFilters] = useState<SheetFilters>({ category: route.params?.category });
  const [options, setOptions] = useState<FilterOptions>({ categories: [], makes: [], models: [], totalCount: 0 });
  const [sheet, setSheet] = useState(false);
  const [grid, setGrid] = useState(false);

  // Opened from another screen with a search / category.
  useEffect(() => {
    if (route.params?.searchTerm !== undefined) {
      const next = { text: route.params.searchTerm, chips: [] };
      setSearch(next);
      setSubmitted(next);
    }
    if (route.params?.category !== undefined) setFilters(f => ({ ...f, category: route.params?.category || undefined }));
  }, [route.params?.searchTerm, route.params?.category]);

  // Pills: the categories and make/model combinations that have live listings near the user.
  useEffect(() => {
    ApiService.getFilterOptions(near, country).then(setOptions);
  }, [near, country]);

  const input = useMemo(() => ({ text: submitted.text, chips: submitted.chips, filters }), [submitted, filters]);
  const { results, facets, total, loading, error, query } = useListingSearch(input);

  const submit = useCallback((next: SearchState) => {
    setSubmitted(next);
    rememberSearch(next);
  }, []);

  const selectedModel = useMemo<ModelPill | null>(
    () => (filters.make && filters.model ? { make: filters.make, model: filters.model } : null),
    [filters.make, filters.model],
  );

  // A selected pill stays visible even when it has no listings any more.
  const categoryPills = useMemo(() => {
    const values = options.categories.map(c => c.value);
    return filters.category && !values.includes(filters.category) ? [filters.category, ...values] : values;
  }, [options.categories, filters.category]);
  const modelPills = useMemo(() => {
    const list: ModelPill[] = options.models.map(m => ({ make: m.make, model: m.model }));
    return selectedModel && !list.some(m => modelKey(m) === modelKey(selectedModel)) ? [selectedModel, ...list] : list;
  }, [options.models, selectedModel]);

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

  const where = near && location ? ` within ${formatRadius(location.radiusKm, country)}` : '';
  const sortLabel = SORTS.find(s => s.value === (filters.sort ?? 'best'))?.label ?? 'Best match';
  const activeFilters = [filters.kind, filters.condition, filters.brandType, filters.partType, filters.year, filters.minPrice, filters.maxPrice, filters.maxDistanceKm, filters.delivery, filters.minSellerRating]
    .filter(v => v !== undefined).length;

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.searchHeader}>
        <SearchBox value={search} onChange={setSearch} onSubmit={submit} country={country} />
      </View>

      {locationOff ? (
        <View style={styles.bannerWrap}>
          <LocationBanner />
        </View>
      ) : null}

      {/* Category Pills */}
      <View style={styles.pillsWrap}>
        <ScrollView horizontal showsHorizontalScrollIndicator={false}>
          {[null, ...categoryPills].map(cat => {
            const active = (filters.category ?? null) === cat;
            return (
              <TouchableOpacity
                key={cat ?? 'all'}
                style={[styles.pill, active && styles.pillActive]}
                onPress={() => setFilters(f => ({ ...f, category: cat ?? undefined }))}
                accessibilityState={{ selected: active }}
              >
                <Text style={[styles.pillText, active && styles.pillTextActive]}>{cat ? categoryLabel(cat) : 'All'}</Text>
              </TouchableOpacity>
            );
          })}
        </ScrollView>
      </View>

      {/* Make / Model Pills + My Garage */}
      <View style={styles.modelsWrap}>
        <ScrollView horizontal showsHorizontalScrollIndicator={false}>
          {garageVehicles.map(v => {
            const active = garage?.id === v.id;
            return (
              <TouchableOpacity key={`g${v.id}`} style={[styles.modelPill, styles.garagePill, active && styles.modelPillActive]}
                onPress={() => selectGarage(active ? null : v.id!)}>
                <Text style={[styles.modelText, active && styles.modelTextActive]}>🚗 {garageLabel(v)}</Text>
              </TouchableOpacity>
            );
          })}
          {[null, ...modelPills].map(mod => {
            const active = mod === null ? selectedModel === null : !!selectedModel && modelKey(selectedModel) === modelKey(mod);
            return (
              <TouchableOpacity
                key={mod ? modelKey(mod) : 'all'}
                style={[styles.modelPill, active && styles.modelPillActive]}
                onPress={() => setFilters(f => ({ ...f, make: mod?.make, model: mod?.model }))}
                accessibilityState={{ selected: active }}
              >
                <Text style={[styles.modelText, active && styles.modelTextActive]}>{mod ? `${mod.make} ${mod.model}` : 'All Models'}</Text>
              </TouchableOpacity>
            );
          })}
        </ScrollView>
      </View>

      {/* Results Header */}
      <View style={styles.resultsHeader}>
        <Text style={styles.resultsCount}>
          {loading ? 'Searching…' : `${total} ${total === 1 ? 'part' : 'parts'} found${where}`}
          {garage ? ` · for your ${garage.year} ${garage.model}` : ''}
        </Text>
        <View style={styles.headerActions}>
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
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  searchHeader: {
    padding: SPACING.md, backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border, zIndex: 10,
  },
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
  garagePill: { borderWidth: 1, borderColor: COLORS.borderHighlight },
  modelPillActive: { backgroundColor: COLORS.badgeBg, borderWidth: 1, borderColor: COLORS.primary },
  modelText: { color: COLORS.textMuted, fontSize: 11, fontWeight: '600' },
  modelTextActive: { color: COLORS.primaryGlow, fontWeight: '700' },
  resultsHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingHorizontal: SPACING.md, paddingVertical: SPACING.sm },
  resultsCount: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '700', flexShrink: 1 },
  headerActions: { flexDirection: 'row', gap: SPACING.md, alignItems: 'center' },
  sortLabel: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '700' },
  error: { color: COLORS.danger, paddingHorizontal: SPACING.md, fontSize: 13 },
  listContent: { padding: SPACING.md, paddingBottom: SPACING.xxl },
});
