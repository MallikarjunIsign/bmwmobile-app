import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { COLORS, RADIUS, SPACING } from '../theme/colors';
import { useLocations } from '../location/LocationContext';

/**
 * Small inline banner while location access is denied or GPS is off. Until then the lists are country-wide
 * (the phone number's country) instead of within 60 km / 60 miles.
 */
export const LocationBanner: React.FC = () => {
  const { locationOff, turnOnLocation } = useLocations();
  if (!locationOff) return null;

  return (
    <View style={styles.banner} accessibilityRole="alert">
      <Text style={styles.icon}>📍</Text>
      <Text style={styles.text}>Turn on location to see parts near you</Text>
      <TouchableOpacity style={styles.button} onPress={() => turnOnLocation()} activeOpacity={0.8}>
        <Text style={styles.buttonText}>Turn on</Text>
      </TouchableOpacity>
    </View>
  );
};

const styles = StyleSheet.create({
  banner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.sm,
    backgroundColor: COLORS.badgeBg,
    borderColor: COLORS.primary,
    borderWidth: 1,
    borderRadius: RADIUS.md,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.sm,
    marginBottom: SPACING.md,
  },
  icon: {
    fontSize: 14,
  },
  text: {
    flex: 1,
    color: COLORS.badgeText,
    fontSize: 13,
    fontWeight: '700',
  },
  button: {
    backgroundColor: COLORS.primary,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 2,
    borderRadius: RADIUS.full,
  },
  buttonText: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '800',
  },
});
