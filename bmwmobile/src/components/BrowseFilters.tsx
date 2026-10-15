import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { useBrowse, type SaleFilter } from '../search/BrowseContext';
import type { ItemTypeFilter } from '../types/listing';

const ITEM_TYPES: { value: ItemTypeFilter; label: string }[] = [
  { value: 'Part', label: 'Parts' },
  { value: 'Vehicle', label: 'Vehicles' },
];

const SALE_TYPES: { value: SaleFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Auction', label: 'Auction' },
  { value: 'FixedPrice', label: 'Direct sale' },
];

/**
 * Home's two filters, also shown on Market: step 1 what – [Parts] [Vehicles]; step 2 how – [All] [Auction]
 * [Direct sale]. They apply to everything below them and to every search.
 */
export const BrowseFilters: React.FC = () => {
  const { itemType, setItemType, saleType, setSaleType } = useBrowse();
  return (
    <View style={styles.wrap}>
      <View style={styles.segment} accessibilityRole="tablist">
        {ITEM_TYPES.map(t => {
          const active = itemType === t.value;
          return (
            <TouchableOpacity
              key={t.value}
              style={[styles.segmentItem, active && styles.segmentActive]}
              onPress={() => setItemType(t.value)}
              activeOpacity={0.8}
              accessibilityRole="tab"
              accessibilityState={{ selected: active }}
            >
              <Text style={[styles.segmentText, active && styles.segmentTextActive]}>{t.label}</Text>
            </TouchableOpacity>
          );
        })}
      </View>
      <View style={styles.chips}>
        {SALE_TYPES.map(s => {
          const active = saleType === s.value;
          return (
            <TouchableOpacity
              key={s.value}
              style={[styles.chip, active && styles.chipActive]}
              onPress={() => setSaleType(s.value)}
              activeOpacity={0.8}
              accessibilityRole="button"
              accessibilityState={{ selected: active }}
            >
              <Text style={[styles.chipText, active && styles.chipTextActive]}>{s.label}</Text>
            </TouchableOpacity>
          );
        })}
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  wrap: { gap: SPACING.sm },
  segment: {
    flexDirection: 'row', backgroundColor: COLORS.surface, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.border, padding: 4,
  },
  segmentItem: { flex: 1, paddingVertical: 9, borderRadius: RADIUS.sm, alignItems: 'center' },
  segmentActive: { backgroundColor: COLORS.primary },
  segmentText: { color: COLORS.textSecondary, fontSize: 14, fontWeight: '700' },
  segmentTextActive: { color: COLORS.text },
  chips: { flexDirection: 'row', gap: SPACING.xs },
  chip: {
    backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2, borderRadius: RADIUS.full,
    borderWidth: 1, borderColor: COLORS.border,
  },
  chipActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  chipText: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '600' },
  chipTextActive: { color: COLORS.text, fontWeight: '800' },
});
