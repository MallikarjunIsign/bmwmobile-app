import React from 'react';
import { View, Text, TouchableOpacity, StyleSheet } from 'react-native';
import { COLORS, RADIUS, SPACING } from '../theme/colors';

interface CheckboxRowProps {
  checked: boolean;
  onToggle: () => void;
  label?: string;
  children?: React.ReactNode;
}

export const CheckboxRow: React.FC<CheckboxRowProps> = ({ checked, onToggle, label, children }) => (
  <TouchableOpacity
    style={styles.row}
    onPress={onToggle}
    activeOpacity={0.7}
    hitSlop={6}
    accessibilityRole="checkbox"
    accessibilityState={{ checked }}
  >
    <View style={[styles.box, checked && styles.boxChecked]}>
      {checked ? <Text style={styles.tick}>✓</Text> : null}
    </View>
    {children ?? <Text style={styles.label}>{label}</Text>}
  </TouchableOpacity>
);

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.sm + 2,
    flexShrink: 1,
  },
  box: {
    width: 22,
    height: 22,
    borderRadius: RADIUS.sm - 2,
    borderWidth: 1.5,
    borderColor: COLORS.borderHighlight,
    backgroundColor: COLORS.card,
    alignItems: 'center',
    justifyContent: 'center',
  },
  boxChecked: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primary,
  },
  tick: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '900',
  },
  label: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontWeight: '600',
    flexShrink: 1,
  },
});
