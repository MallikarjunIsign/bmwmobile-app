import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';

interface BadgeProps {
  label: string;
  variant?: 'primary' | 'success' | 'danger' | 'warning' | 'info' | 'dark';
  size?: 'sm' | 'md';
}

export const Badge: React.FC<BadgeProps> = ({ label, variant = 'primary', size = 'md' }) => {
  const getColors = () => {
    switch (variant) {
      case 'success':
        return { bg: 'rgba(34, 164, 71, 0.15)', text: COLORS.success, border: 'rgba(34, 164, 71, 0.4)' };
      case 'danger':
        return { bg: 'rgba(239, 68, 68, 0.15)', text: COLORS.danger, border: 'rgba(239, 68, 68, 0.4)' };
      case 'warning':
        return { bg: 'rgba(245, 176, 20, 0.15)', text: COLORS.warning, border: 'rgba(245, 176, 20, 0.4)' };
      case 'info':
        return { bg: 'rgba(59, 130, 246, 0.15)', text: COLORS.containerBlue, border: 'rgba(59, 130, 246, 0.4)' };
      case 'dark':
        return { bg: COLORS.cardSecondary, text: COLORS.textSecondary, border: COLORS.border };
      case 'primary':
      default:
        return { bg: COLORS.badgeBg, text: COLORS.badgeText, border: 'rgba(161, 0, 255, 0.4)' };
    }
  };

  const { bg, text, border } = getColors();

  return (
    <View style={[
      styles.badge, 
      { backgroundColor: bg, borderColor: border },
      size === 'sm' && styles.badgeSm
    ]}>
      <Text style={[styles.badgeText, { color: text }, size === 'sm' && styles.textSm]}>
        {label}
      </Text>
    </View>
  );
};

const styles = StyleSheet.create({
  badge: {
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 2,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    alignSelf: 'flex-start',
  },
  badgeSm: {
    paddingHorizontal: SPACING.sm,
    paddingVertical: 2,
  },
  badgeText: {
    fontSize: 12,
    fontWeight: '700',
    letterSpacing: 0.5,
    textTransform: 'uppercase',
  },
  textSm: {
    fontSize: 10,
  }
});
