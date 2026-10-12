import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, Modal, ScrollView, TouchableOpacity, TextInput } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import type { FacetValue, ListingSearchQuery } from '../types/listing';
import { KM_PER_MILE } from '../utils/geo';
import { label } from '../utils/time';

export type SheetFilters = Pick<
  ListingSearchQuery,
  'category' | 'partType' | 'make' | 'model' | 'year' | 'condition' | 'brandType' | 'kind' | 'minPrice' | 'maxPrice' | 'maxDistanceKm' | 'sort' | 'delivery' | 'minSellerRating'
>;

export const SORTS: { value: NonNullable<ListingSearchQuery['sort']>; label: string }[] = [
  { value: 'best', label: 'Best match' },
  { value: 'endingSoon', label: 'Ending soon' },
  { value: 'newest', label: 'Newest' },
  { value: 'priceAsc', label: 'Price low → high' },
  { value: 'nearest', label: 'Nearest' },
  { value: 'topRated', label: 'Top-rated sellers' },
];

interface FilterSheetProps {
  visible: boolean;
  onClose: () => void;
  value: SheetFilters;
  facets: Record<string, FacetValue[]>;
  onApply: (value: SheetFilters) => void;
  /** 'km' or 'mi' (USA). */
  unit: 'km' | 'mi';
  currencySymbol: string;
  /** No distance filter while the location is off. */
  hasLocation: boolean;
}

/** Filter sheet with facets: Delivery, Category, Part type, Make, Model, Year, Condition, Brand type, Price, Distance, Listing kind; and sort. */
export const FilterSheet: React.FC<FilterSheetProps> = ({ visible, onClose, value, facets, onApply, unit, currencySymbol, hasLocation }) => {
  const insets = useSafeAreaInsets();
  const [draft, setDraft] = useState<SheetFilters>(value);
  useEffect(() => {
    if (visible) setDraft(value);
  }, [visible]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (patch: Partial<SheetFilters>) => setDraft(d => ({ ...d, ...patch }));

  const renderFacet = ({ title, name, field, format }: { title: string; name: string; field: keyof SheetFilters; format?: (v: string) => string }) => {
    const values = facets[name] ?? [];
    const current = draft[field] as string | number | undefined;
    if (values.length === 0 && current === undefined) return null;
    return (
      <View style={styles.section}>
        <Text style={styles.sectionTitle}>{title}</Text>
        <View style={styles.chips}>
          {values.map(v => {
            const active = String(current ?? '') === v.value;
            return (
              <TouchableOpacity key={v.value} style={[styles.chip, active && styles.chipActive]}
                onPress={() => set({ [field]: active ? undefined : field === 'year' ? Number(v.value) : v.value } as any)}>
                <Text style={[styles.chipText, active && styles.chipTextActive]}>{format ? format(v.value) : label(v.value)} ({v.count})</Text>
              </TouchableOpacity>
            );
          })}
        </View>
      </View>
    );
  };

  const distances = unit === 'mi' ? [10, 25, 60] : [10, 25, 60];
  const modelValue = draft.make && draft.model ? `${draft.make}|${draft.model}` : undefined;

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <View style={styles.backdrop}>
        <View style={[styles.sheet, { paddingBottom: insets.bottom + SPACING.md }]}>
          <View style={styles.head}>
            <Text style={styles.title}>Filters</Text>
            <TouchableOpacity onPress={() => setDraft({ sort: draft.sort })}>
              <Text style={styles.reset}>Reset</Text>
            </TouchableOpacity>
          </View>
          <ScrollView>
            <View style={styles.section}>
              <Text style={styles.sectionTitle}>Sort</Text>
              <View style={styles.chips}>
                {SORTS.filter(s => hasLocation || s.value !== 'nearest').map(s => (
                  <TouchableOpacity key={s.value} style={[styles.chip, (draft.sort ?? 'best') === s.value && styles.chipActive]} onPress={() => set({ sort: s.value })}>
                    <Text style={[styles.chipText, (draft.sort ?? 'best') === s.value && styles.chipTextActive]}>{s.label}</Text>
                  </TouchableOpacity>
                ))}
              </View>
            </View>
            <View style={styles.section}>
              <Text style={styles.sectionTitle}>Delivery</Text>
              <View style={styles.chips}>
                {(['Pickup', 'Shipping'] as const).map(d => {
                  const active = draft.delivery === d;
                  const count = facets.delivery?.find(f => f.value === d)?.count;
                  return (
                    <TouchableOpacity key={d} style={[styles.chip, active && styles.chipActive]} onPress={() => set({ delivery: active ? undefined : d })}
                      accessibilityState={{ selected: active }}>
                      <Text style={[styles.chipText, active && styles.chipTextActive]}>
                        {d === 'Pickup' ? '📍 Pickup' : '🚚 Shipping'}{count !== undefined ? ` (${count})` : ''}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            </View>
            <View style={styles.section}>
              <Text style={styles.sectionTitle}>Seller rating</Text>
              <View style={styles.chips}>
                <TouchableOpacity style={[styles.chip, draft.minSellerRating === 4 && styles.chipActive]}
                  onPress={() => set({ minSellerRating: draft.minSellerRating === 4 ? undefined : 4 })}
                  accessibilityState={{ selected: draft.minSellerRating === 4 }}>
                  <Text style={[styles.chipText, draft.minSellerRating === 4 && styles.chipTextActive]}>⭐ 4★ & up</Text>
                </TouchableOpacity>
              </View>
            </View>
            {renderFacet({ title: 'Listing kind', name: 'kind', field: 'kind' })}
            {renderFacet({ title: 'Category', name: 'category', field: 'category' })}
            {renderFacet({ title: 'Part type', name: 'partType', field: 'partType' })}
            {renderFacet({ title: 'Make', name: 'make', field: 'make' })}
            {(facets.model ?? []).length > 0 ? (
              <View style={styles.section}>
                <Text style={styles.sectionTitle}>Model</Text>
                <View style={styles.chips}>
                  {facets.model.map(v => {
                    const active = modelValue === v.value;
                    const [make, model] = v.value.split('|');
                    return (
                      <TouchableOpacity key={v.value} style={[styles.chip, active && styles.chipActive]}
                        onPress={() => set(active ? { model: undefined } : { make, model })}>
                        <Text style={[styles.chipText, active && styles.chipTextActive]}>{make} {model} ({v.count})</Text>
                      </TouchableOpacity>
                    );
                  })}
                </View>
              </View>
            ) : null}
            {renderFacet({ title: 'Year', name: 'year', field: 'year', format: v => v })}
            {renderFacet({ title: 'Condition', name: 'condition', field: 'condition' })}
            {renderFacet({ title: 'Brand type', name: 'brandType', field: 'brandType' })}
            <View style={styles.section}>
              <Text style={styles.sectionTitle}>Price ({currencySymbol})</Text>
              <View style={styles.priceRow}>
                <TextInput style={styles.price} placeholder="Min" placeholderTextColor={COLORS.textMuted} keyboardType="number-pad"
                  value={draft.minPrice != null ? String(draft.minPrice) : ''} onChangeText={v => set({ minPrice: v ? parseInt(v, 10) : undefined })} />
                <Text style={styles.dash}>–</Text>
                <TextInput style={styles.price} placeholder="Max" placeholderTextColor={COLORS.textMuted} keyboardType="number-pad"
                  value={draft.maxPrice != null ? String(draft.maxPrice) : ''} onChangeText={v => set({ maxPrice: v ? parseInt(v, 10) : undefined })} />
              </View>
            </View>
            {hasLocation ? (
              <View style={styles.section}>
                <Text style={styles.sectionTitle}>Distance</Text>
                <View style={styles.chips}>
                  {distances.map(d => {
                    const km = unit === 'mi' ? Math.round(d * KM_PER_MILE) : d;
                    const active = draft.maxDistanceKm === km;
                    return (
                      <TouchableOpacity key={d} style={[styles.chip, active && styles.chipActive]} onPress={() => set({ maxDistanceKm: active ? undefined : km })}>
                        <Text style={[styles.chipText, active && styles.chipTextActive]}>within {d} {unit}</Text>
                      </TouchableOpacity>
                    );
                  })}
                </View>
              </View>
            ) : null}
          </ScrollView>
          <View style={styles.footer}>
            <TouchableOpacity style={styles.cancel} onPress={onClose}>
              <Text style={styles.cancelText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.apply} onPress={() => { onApply(draft); onClose(); }}>
              <Text style={styles.applyText}>Show results</Text>
            </TouchableOpacity>
          </View>
        </View>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'flex-end' },
  sheet: { backgroundColor: COLORS.surface, borderTopLeftRadius: RADIUS.xl, borderTopRightRadius: RADIUS.xl, maxHeight: '88%', paddingTop: SPACING.md },
  head: { flexDirection: 'row', justifyContent: 'space-between', paddingHorizontal: SPACING.lg, marginBottom: SPACING.sm },
  title: { color: COLORS.text, fontSize: 18, fontWeight: '800' },
  reset: { color: COLORS.primaryGlow, fontWeight: '700' },
  section: { paddingHorizontal: SPACING.lg, paddingVertical: SPACING.sm },
  sectionTitle: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginBottom: SPACING.xs, textTransform: 'uppercase' },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: SPACING.xs },
  chip: { backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2, borderRadius: RADIUS.full, borderWidth: 1, borderColor: COLORS.border },
  chipActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  chipText: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '600' },
  chipTextActive: { color: COLORS.text, fontWeight: '800' },
  priceRow: { flexDirection: 'row', alignItems: 'center', gap: SPACING.sm },
  price: { flex: 1, backgroundColor: COLORS.card, borderRadius: RADIUS.md, color: COLORS.text, paddingHorizontal: SPACING.md, height: 44, borderWidth: 1, borderColor: COLORS.border },
  dash: { color: COLORS.textMuted },
  footer: { flexDirection: 'row', gap: SPACING.sm, paddingHorizontal: SPACING.lg, paddingTop: SPACING.md, borderTopWidth: 1, borderTopColor: COLORS.border },
  cancel: { paddingVertical: 14, paddingHorizontal: SPACING.lg, borderRadius: RADIUS.md, backgroundColor: COLORS.cardSecondary },
  cancelText: { color: COLORS.text, fontWeight: '700' },
  apply: { flex: 1, paddingVertical: 14, borderRadius: RADIUS.md, backgroundColor: COLORS.primary, alignItems: 'center' },
  applyText: { color: COLORS.text, fontWeight: '800' },
});
