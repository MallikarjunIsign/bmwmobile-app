import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { COLORS, RADIUS, SPACING } from '../theme/colors';

interface NoticeProps {
  tone: 'error' | 'success' | 'info';
  message: string;
}

const TONES = {
  error: { icon: '⚠️', color: COLORS.danger, bg: 'rgba(239, 68, 68, 0.12)' },
  success: { icon: '✅', color: COLORS.successGlow, bg: 'rgba(34, 164, 71, 0.12)' },
  info: { icon: '💡', color: COLORS.badgeText, bg: COLORS.badgeBg },
};

/** A short message box for form errors, confirmations and hints. */
export const Notice: React.FC<NoticeProps> = ({ tone, message }) => {
  const t = TONES[tone];
  return (
    <View style={[styles.box, { backgroundColor: t.bg, borderColor: t.color }]} accessibilityRole="alert">
      <Text style={styles.icon}>{t.icon}</Text>
      <Text style={[styles.text, { color: t.color }]}>{message}</Text>
    </View>
  );
};

const styles = StyleSheet.create({
  box: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: SPACING.sm,
    padding: SPACING.md - 4,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    marginTop: SPACING.md,
  },
  icon: {
    fontSize: 14,
  },
  text: {
    flex: 1,
    fontSize: 13,
    fontWeight: '600',
    lineHeight: 19,
  },
});
