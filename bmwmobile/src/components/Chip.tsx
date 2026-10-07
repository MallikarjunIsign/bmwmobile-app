import React from 'react';
import { Text, TouchableOpacity, StyleSheet } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';

interface ChipProps {
  label: string;
  selected?: boolean;
  onPress: () => void;
}

/** Pill button with the same look as the Home "Filter by your garage" chips. */
export const Chip: React.FC<ChipProps> = ({ label, selected, onPress }) => (
  <TouchableOpacity
    style={[styles.chip, selected && styles.chipActive]}
    onPress={onPress}
    activeOpacity={0.8}
    accessibilityRole="button"
    accessibilityState={{ selected: !!selected }}
  >
    <Text style={[styles.text, selected && styles.textActive]}>{label}</Text>
  </TouchableOpacity>
);

const styles = StyleSheet.create({
  chip: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 4,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  chipActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  text: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontWeight: '600',
  },
  textActive: {
    color: COLORS.text,
    fontWeight: '800',
  },
});
