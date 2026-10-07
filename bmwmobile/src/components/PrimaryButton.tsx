import React from 'react';
import { Text, TouchableOpacity, ActivityIndicator, StyleSheet, StyleProp, ViewStyle } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';

interface PrimaryButtonProps {
  label: string;
  onPress: () => void;
  loading?: boolean;
  disabled?: boolean;
  /** 'outline' = the dark secondary button (e.g. "Set Pickup Port & Garage"). */
  variant?: 'primary' | 'outline';
  icon?: React.ReactNode;
  style?: StyleProp<ViewStyle>;
}

/** The app's full-width button: purple uppercase primary, or dark outlined secondary. */
export const PrimaryButton: React.FC<PrimaryButtonProps> = ({ label, onPress, loading, disabled, variant = 'primary', icon, style }) => {
  const isDisabled = disabled || loading;
  const primary = variant === 'primary';
  return (
    <TouchableOpacity
      style={[primary ? styles.primary : styles.outline, isDisabled && styles.disabled, style]}
      onPress={onPress}
      disabled={isDisabled}
      activeOpacity={0.8}
      accessibilityRole="button"
      accessibilityState={{ disabled: !!isDisabled, busy: !!loading }}
    >
      {loading ? (
        <ActivityIndicator color={COLORS.text} />
      ) : (
        <>
          {icon}
          <Text style={primary ? styles.primaryText : styles.outlineText}>{label}</Text>
        </>
      )}
    </TouchableOpacity>
  );
};

const styles = StyleSheet.create({
  primary: {
    flexDirection: 'row',
    justifyContent: 'center',
    gap: SPACING.sm,
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    minHeight: 50,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  primaryText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 1,
  },
  outline: {
    flexDirection: 'row',
    justifyContent: 'center',
    gap: SPACING.sm,
    backgroundColor: COLORS.card,
    paddingVertical: 14,
    minHeight: 50,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  outlineText: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '700',
  },
  disabled: {
    opacity: 0.6,
  },
});
