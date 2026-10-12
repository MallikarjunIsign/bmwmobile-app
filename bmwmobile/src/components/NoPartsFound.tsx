import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { COLORS, RADIUS, SPACING } from '../theme/colors';

/** Shown exactly like this whenever a search or filter finds nothing (the search is never relaxed silently). */
export const NO_PARTS_TITLE = 'There are no parts available for this search.';
export const NO_PARTS_TEXT = 'Create a custom order for your part and get notified when the part is available.';

interface NoPartsFoundProps {
  onCreateOrder: () => void;
}

export const NoPartsFound: React.FC<NoPartsFoundProps> = ({ onCreateOrder }) => (
  <View style={styles.box}>
    <Text style={styles.icon}>🔍</Text>
    <Text style={styles.title}>{NO_PARTS_TITLE}</Text>
    <Text style={styles.text}>{NO_PARTS_TEXT}</Text>
    <TouchableOpacity style={styles.button} onPress={onCreateOrder} activeOpacity={0.8} accessibilityRole="button">
      <Text style={styles.buttonText}>Create custom order</Text>
    </TouchableOpacity>
  </View>
);

const styles = StyleSheet.create({
  box: {
    alignItems: 'center',
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    padding: SPACING.lg,
    marginVertical: SPACING.md,
  },
  icon: {
    fontSize: 36,
    marginBottom: SPACING.sm,
  },
  title: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
    textAlign: 'center',
  },
  text: {
    color: COLORS.textMuted,
    fontSize: 13,
    textAlign: 'center',
    lineHeight: 19,
    marginTop: SPACING.xs,
    marginBottom: SPACING.lg,
  },
  button: {
    backgroundColor: COLORS.primary,
    paddingHorizontal: SPACING.lg,
    paddingVertical: SPACING.md - 2,
    borderRadius: RADIUS.md,
  },
  buttonText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '800',
  },
});
