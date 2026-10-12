import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { COLORS, SPACING } from '../theme/colors';

/** ★★★★☆ – a rating shown as stars (rounded to the nearest whole star). */
export const Stars: React.FC<{ value: number; size?: number }> = ({ value, size = 14 }) => {
  const full = Math.round(value);
  return (
    <Text style={[styles.stars, { fontSize: size }]} accessibilityLabel={`${value} out of 5 stars`}>
      {'★'.repeat(full)}
      <Text style={styles.empty}>{'★'.repeat(5 - full)}</Text>
    </Text>
  );
};

/** Tap 1–5 stars. Optional pickers can be cleared by tapping the chosen star again. */
export const StarPicker: React.FC<{
  label: string;
  value?: number | null;
  onChange: (value: number | null) => void;
  optional?: boolean;
  hint?: string;
}> = ({ label, value, onChange, optional, hint }) => (
  <View style={styles.picker}>
    <Text style={styles.label}>
      {label.toUpperCase()}
      {optional ? <Text style={styles.optional}> (OPTIONAL)</Text> : null}
    </Text>
    {hint ? <Text style={styles.hint}>{hint}</Text> : null}
    <View style={styles.row}>
      {[1, 2, 3, 4, 5].map(n => (
        <TouchableOpacity
          key={n}
          onPress={() => onChange(optional && value === n ? null : n)}
          hitSlop={6}
          accessibilityRole="button"
          accessibilityLabel={`${label}: ${n} star${n === 1 ? '' : 's'}`}
          accessibilityState={{ selected: (value ?? 0) >= n }}
        >
          <Text style={[styles.big, (value ?? 0) >= n ? styles.on : styles.off]}>★</Text>
        </TouchableOpacity>
      ))}
    </View>
  </View>
);

const styles = StyleSheet.create({
  stars: { color: COLORS.warningGlow, letterSpacing: 1 },
  empty: { color: COLORS.border },
  picker: { marginTop: SPACING.md },
  label: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1 },
  optional: { color: COLORS.textMuted, fontWeight: '600' },
  hint: { color: COLORS.textMuted, fontSize: 12, marginTop: 2 },
  row: { flexDirection: 'row', gap: SPACING.sm, marginTop: SPACING.xs },
  big: { fontSize: 32 },
  on: { color: COLORS.warningGlow },
  off: { color: COLORS.border },
});
