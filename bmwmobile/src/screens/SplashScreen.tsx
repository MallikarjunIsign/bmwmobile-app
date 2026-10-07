import React from 'react';
import { View, Text, StyleSheet, Image, TouchableOpacity, SafeAreaView } from 'react-native';
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
          <Text style={styles.logoText}>OKOH</Text>
          <Text style={styles.subLogoText}>PARTS</Text>
        </View>

        <Text style={styles.tagline}>DIRECT BMW GERMAN AUCTIONS & NIGERIAN CONTAINER LOGISTICS</Text>
        
        <View style={styles.pillContainer}>
          <Text style={styles.pill}>🇩🇪 Direct Munich Sourcing</Text>
          <Text style={styles.pill}>🚢 Lagos Apapa & Tin Can Shipping</Text>
          <Text style={styles.pill}>⚡ Custom Container Part Feasibility</Text>
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

        <TouchableOpacity 
          style={styles.secondaryBtn} 
          onPress={() => navigation.navigate('ProfilePreferences')}
          activeOpacity={0.8}
        >
          <Text style={styles.secondaryBtnText}>Set Pickup Port & Garage</Text>
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
    fontSize: 28,
    fontWeight: '900',
    letterSpacing: 2,
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
  secondaryBtn: {
    backgroundColor: COLORS.card,
    paddingVertical: SPACING.md,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  secondaryBtnText: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '700',
  },
});
