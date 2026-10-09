import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';
import { useMoney } from '../location/useMoney';

type Props = NativeStackScreenProps<RootStackParamList, 'HighestBidder'>;

export const HighestBidderScreen: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const { auction } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>🏆</Text>
        </View>

        <Badge label="YOU ARE HIGHEST BIDDER" variant="success" />

        <Text style={styles.title}>{auction.title}</Text>

        <View style={styles.card}>
          <Text style={styles.cardLabel}>YOUR CONFIRMED HIGHEST BID</Text>
          <Text style={styles.bidAmount}>{money(auction.currentHighBid)}</Text>

          <View style={styles.row}>
            <Text style={styles.key}>Status:</Text>
            <Text style={styles.valSuccess}>Active High Bidder ✓</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Auto-Bid Protection:</Text>
            <Text style={styles.val}>Active (Ceiling: {money(auction.userMaxAutoBid ?? 16000)})</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Target Shipment Container:</Text>
            <Text style={styles.val}>{auction.containerNumber || 'CN-2026-09'}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Port Destination:</Text>
            <Text style={styles.val}>{auction.pickupPort || 'Lagos Apapa Port'}</Text>
          </View>
        </View>

        <View style={styles.alertNotice}>
          <Text style={styles.alertTitle}>🔔 Real-Time SMS & Push Alerts Enabled</Text>
          <Text style={styles.alertText}>
            We will notify you instantly if another buyer places a higher bid so you can counter bid before the auction ends.
          </Text>
        </View>

        <TouchableOpacity 
          style={styles.primaryBtn} 
          onPress={() => navigation.navigate('MainTabs', { screen: 'MyBids' }, { pop: true })}
          activeOpacity={0.8}
        >
          <Text style={styles.primaryBtnText}>VIEW MY ACTIVE BIDS</Text>
        </TouchableOpacity>

        <TouchableOpacity 
          style={styles.secondaryBtn} 
          onPress={() => navigation.navigate('MainTabs', { screen: 'Home' }, { pop: true })}
          activeOpacity={0.8}
        >
          <Text style={styles.secondaryBtnText}>Back to Dashboard</Text>
        </TouchableOpacity>
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  content: {
    padding: SPACING.lg,
    alignItems: 'center',
  },
  iconCircle: {
    width: 80,
    height: 80,
    borderRadius: 40,
    backgroundColor: 'rgba(34, 164, 71, 0.15)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 2,
    borderColor: COLORS.success,
    marginBottom: SPACING.md,
    marginTop: SPACING.md,
  },
  iconText: {
    fontSize: 40,
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    textAlign: 'center',
    marginVertical: SPACING.md,
    lineHeight: 24,
  },
  card: {
    width: '100%',
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    padding: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginBottom: SPACING.md,
  },
  cardLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    textAlign: 'center',
  },
  bidAmount: {
    color: COLORS.success,
    fontSize: 32,
    fontWeight: '900',
    textAlign: 'center',
    marginVertical: SPACING.xs,
  },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: 6,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  key: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  val: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '700',
  },
  valSuccess: {
    color: COLORS.success,
    fontSize: 12,
    fontWeight: '800',
  },
  alertNotice: {
    width: '100%',
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.lg,
  },
  alertTitle: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
    fontSize: 13,
    marginBottom: 4,
  },
  alertText: {
    color: COLORS.textMuted,
    fontSize: 12,
    lineHeight: 16,
  },
  primaryBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginBottom: SPACING.sm,
  },
  primaryBtnText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 1,
  },
  secondaryBtn: {
    width: '100%',
    backgroundColor: COLORS.card,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  secondaryBtnText: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
  },
});
