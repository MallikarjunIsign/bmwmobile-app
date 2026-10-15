import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';

type Props = NativeStackScreenProps<RootStackParamList, 'Splash'>;

export const SplashScreen: React.FC<Props> = ({ navigation }) => {
  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.content}>
        {/* Brand Glow Emblem */}
        <View style={styles.logoBadge}>
          <Text style={styles.logoText} numberOfLines={1} adjustsFontSizeToFit>ShopCar</Text>
          <Text style={styles.subLogoText}>PARTS</Text>
        </View>

        <Text style={styles.tagline}>BUY AND SELL CAR PARTS & VEHICLES BY AUCTION</Text>
        
        <View style={styles.pillContainer}>
          <Text style={styles.pill}>📍 Parts near you</Text>
          <Text style={styles.pill}>⚡ Live bidding</Text>
          <Text style={styles.pill}>🔔 Custom orders – get notified</Text>
        </View>
      </View>

      <View style={styles.footer}>
        <TouchableOpacity 
          style={styles.primaryBtn} 
          onPress={() => navigation.navigate('Login')}
          activeOpacity={0.8}
        >
          <Text style={styles.primaryBtnText}>ENTER MARKETPLACE</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
    justifyContent: 'space-between',
    paddingHorizontal: SPACING.lg,
  },
  content: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
  },
  logoBadge: {
    width: 140,
    height: 140,
    borderRadius: 70,
    backgroundColor: COLORS.surface,
    borderWidth: 3,
    borderColor: COLORS.primaryGlow,
    justifyContent: 'center',
    alignItems: 'center',
    shadowColor: COLORS.primary,
    shadowOffset: { width: 0, height: 10 },
    shadowOpacity: 0.8,
    shadowRadius: 20,
    elevation: 10,
    marginBottom: SPACING.xl,
  },
  logoText: {
    color: COLORS.text,
    fontSize: 24,
    fontWeight: '900',
    letterSpacing: 1,
    // Shrinks to fit the round badge on one line.
    maxWidth: 120,
  },
  subLogoText: {
    color: COLORS.primaryGlow,
    fontSize: 14,
    fontWeight: '800',
    letterSpacing: 4,
  },
  tagline: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '700',
    textAlign: 'center',
    letterSpacing: 1,
    lineHeight: 22,
    marginHorizontal: SPACING.md,
  },
  pillContainer: {
    marginTop: SPACING.xl,
    gap: SPACING.sm,
    width: '100%',
    alignItems: 'center',
  },
  pill: {
    color: COLORS.textSecondary,
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.sm,
    borderRadius: RADIUS.full,
    fontSize: 13,
    fontWeight: '600',
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  footer: {
    paddingBottom: SPACING.xl,
    gap: SPACING.md,
  },
  primaryBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: SPACING.md + 2,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  primaryBtnText: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
