import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { categoryIcon, categoryLabel } from '../utils/categories';
import type { FacetValue } from '../types/listing';

interface CategoryTilesProps {
  /** Category facet from the search (only categories with listings). */
  categories: FacetValue[];
  /** The chosen category (highlighted). */
  selected?: string;
  onPress: (category: string) => void;
}

/** Part categories as tiles with the number of available parts: "🛑 Brakes (24)". Two per row. */
export const CategoryTiles: React.FC<CategoryTilesProps> = ({ categories, selected, onPress }) => (
  <View style={styles.grid}>
    {categories.filter(c => c.count > 0).map(c => (
      <TouchableOpacity
        key={c.value}
        style={[styles.tile, selected === c.value && styles.tileSelected]}
        onPress={() => onPress(c.value)}
        activeOpacity={0.8}
        accessibilityRole="button"
        accessibilityLabel={`${categoryLabel(c.value)}, ${c.count} available`}
        accessibilityState={{ selected: selected === c.value }}
        testID={`category-tile-${c.value}`}
      >
        <Text style={styles.icon}>{categoryIcon(c.value)}</Text>
        <Text style={styles.name} numberOfLines={2}>
          {categoryLabel(c.value)} <Text style={styles.count}>({c.count})</Text>
        </Text>
      </TouchableOpacity>
    ))}
  </View>
);

const styles = StyleSheet.create({
  grid: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'space-between', rowGap: SPACING.sm },
  tile: {
    width: '48.5%', flexDirection: 'row', alignItems: 'center', gap: SPACING.sm, backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.border, paddingHorizontal: SPACING.md, paddingVertical: SPACING.md,
  },
  tileSelected: { borderColor: COLORS.primaryGlow, backgroundColor: COLORS.badgeBg },
  icon: { fontSize: 22 },
  name: { color: COLORS.text, fontSize: 13, fontWeight: '700', flexShrink: 1 },
  count: { color: COLORS.primaryGlow, fontWeight: '800' },
});
