import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { COLORS, SPACING } from '../theme/colors';
import { PASSWORD_RULES } from '../utils/validation';

/** Live password rules: each line turns green when it's met. */
export const PasswordChecklist: React.FC<{ password: string }> = ({ password }) => (
  <View style={styles.list} accessibilityLabel="Password requirements">
    {PASSWORD_RULES.map(rule => {
      const ok = rule.test(password);
      return (
        <View key={rule.id} style={styles.row}>
          <Text style={[styles.mark, ok && styles.ok]}>{ok ? '✓' : '○'}</Text>
          <Text style={[styles.text, ok && styles.ok]}>{rule.label}</Text>
        </View>
      );
    })}
  </View>
);

const styles = StyleSheet.create({
  list: {
    marginTop: SPACING.sm,
    gap: 4,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.sm,
  },
  mark: {
    width: 14,
    color: COLORS.textMuted,
    fontSize: 13,
    fontWeight: '900',
    textAlign: 'center',
  },
  text: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '600',
  },
  ok: {
    color: COLORS.successGlow,
  },
});
