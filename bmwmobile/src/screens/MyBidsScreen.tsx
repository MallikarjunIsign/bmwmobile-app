import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, SafeAreaView } from 'react-native';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { MOCK_AUCTIONS } from '../services/api';
import { AuctionCard } from '../components/AuctionCard';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'MyBids'>,
  NativeStackScreenProps<RootStackParamList>
>;

export const MyBidsScreen: React.FC<Props> = ({ navigation }) => {
  const [activeTab, setActiveTab] = useState<'Active' | 'Won' | 'Ended'>('Active');

  const activeBids = MOCK_AUCTIONS;

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>My Bids & Auction Trackers</Text>
      </View>

      {/* Tabs */}
      <View style={styles.tabBar}>
        {(['Active', 'Won', 'Ended'] as const).map(tab => (
          <TouchableOpacity
            key={tab}
            style={[styles.tab, activeTab === tab && styles.tabActive]}
            onPress={() => setActiveTab(tab)}
          >
            <Text style={[styles.tabText, activeTab === tab && styles.tabTextActive]}>
              {tab} Bids {tab === 'Active' ? `(${activeBids.length})` : ''}
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      <ScrollView contentContainerStyle={styles.scroll}>
        {activeTab === 'Active' && (
          <View>
            <View style={styles.statusSummary}>
              <View style={styles.summaryBox}>
                <Text style={styles.boxValSuccess}>1</Text>
                <Text style={styles.boxKey}>Highest Bidder</Text>
              </View>
              <View style={styles.summaryBox}>
                <Text style={styles.boxValWarning}>1</Text>
                <Text style={styles.boxKey}>Outbid Alert</Text>
              </View>
              <View style={styles.summaryBox}>
                <Text style={styles.boxValPrimary}>1</Text>
                <Text style={styles.boxKey}>Ending Soon</Text>
              </View>
            </View>

            {/* Quick Demo Outbid State Banner */}
            <TouchableOpacity 
              style={styles.outbidBanner}
              onPress={() => navigation.navigate('Outbid', { auction: MOCK_AUCTIONS[1] })}
              activeOpacity={0.9}
            >
              <View style={{ flex: 1 }}>
                <Text style={styles.outbidTitle}>⚠️ OUTBID ALERT: 2019 BMW 330i LaserLight</Text>
                <Text style={styles.outbidSub}>Current bid: $2,450 by autoworks_lagos. Tap to counter bid.</Text>
              </View>
              <Text style={styles.outbidArrow}>Outbid →</Text>
            </TouchableOpacity>

            {activeBids.map(auction => (
              <AuctionCard
                key={auction.id}
                auction={auction}
                onPress={() => navigation.navigate('PartDetails', { auctionId: auction.id, auction })}
                onBidPress={() => navigation.navigate('PlaceBidModal', { auction })}
              />
            ))}
          </View>
        )}

        {activeTab === 'Won' && (
          <View style={styles.wonCard}>
            <Text style={styles.wonTitle}>🎉 AUCTION WON: 2021 BMW M4 Engine Assembly</Text>
            <Text style={styles.wonAmount}>Winning Bid: $14,500</Text>
            <Text style={styles.wonPort}>Destination: Lagos Apapa Port (Container MSCU7821940)</Text>

            <TouchableOpacity 
              style={styles.checkoutBtn}
              onPress={() => navigation.navigate('CompletePurchase', { auctionId: 'auc-bmw-m4-01' })}
            >
              <Text style={styles.checkoutBtnText}>COMPLETE CHECKOUT & GENERATE PICKUP PASS</Text>
            </TouchableOpacity>
          </View>
        )}

        {activeTab === 'Ended' && (
          <View style={styles.endedCard}>
            <Text style={styles.endedTitle}>2020 BMW X3 M-Sport Steering Wheel</Text>
            <Text style={styles.endedStatus}>Ended • Sold for $850</Text>
            <TouchableOpacity 
              style={styles.reorderBtn}
              onPress={() => navigation.navigate('OrderPartRequest', { prefillPart: 'BMW X3 M Steering Wheel' })}
            >
              <Text style={styles.reorderText}>Request Similar Part for Next Container →</Text>
            </TouchableOpacity>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  header: {
    padding: SPACING.md,
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
  },
  tabBar: {
    flexDirection: 'row',
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  tab: {
    flex: 1,
    paddingVertical: SPACING.md,
    alignItems: 'center',
    borderBottomWidth: 2,
    borderBottomColor: 'transparent',
  },
  tabActive: {
    borderBottomColor: COLORS.primaryGlow,
  },
  tabText: {
    color: COLORS.textMuted,
    fontSize: 13,
    fontWeight: '600',
  },
  tabTextActive: {
    color: COLORS.text,
    fontWeight: '800',
  },
  scroll: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  statusSummary: {
    flexDirection: 'row',
    gap: SPACING.xs,
    marginBottom: SPACING.md,
  },
  summaryBox: {
    flex: 1,
    backgroundColor: COLORS.surface,
    padding: SPACING.sm,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  boxValSuccess: {
    color: COLORS.success,
    fontSize: 20,
    fontWeight: '900',
  },
  boxValWarning: {
    color: COLORS.danger,
    fontSize: 20,
    fontWeight: '900',
  },
  boxValPrimary: {
    color: COLORS.warningGlow,
    fontSize: 20,
    fontWeight: '900',
  },
  boxKey: {
    color: COLORS.textMuted,
    fontSize: 11,
    marginTop: 2,
  },
  outbidBanner: {
    backgroundColor: 'rgba(239, 68, 68, 0.15)',
    borderWidth: 1,
    borderColor: COLORS.danger,
    borderRadius: RADIUS.lg,
    padding: SPACING.md,
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: SPACING.md,
  },
  outbidTitle: {
    color: COLORS.danger,
    fontWeight: '800',
    fontSize: 13,
  },
  outbidSub: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginTop: 2,
  },
  outbidArrow: {
    color: COLORS.danger,
    fontWeight: '800',
    fontSize: 13,
    marginLeft: SPACING.xs,
  },
  wonCard: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.success,
  },
  wonTitle: {
    color: COLORS.text,
    fontWeight: '800',
    fontSize: 15,
  },
  wonAmount: {
    color: COLORS.success,
    fontSize: 20,
    fontWeight: '900',
    marginVertical: SPACING.xs,
  },
  wonPort: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginBottom: SPACING.md,
  },
  checkoutBtn: {
    backgroundColor: COLORS.success,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  checkoutBtnText: {
    color: COLORS.text,
    fontWeight: '900',
    fontSize: 13,
  },
  endedCard: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  endedTitle: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '700',
  },
  endedStatus: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginVertical: SPACING.xs,
  },
  reorderBtn: {
    backgroundColor: COLORS.cardSecondary,
    paddingVertical: 10,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginTop: SPACING.xs,
    borderWidth: 1,
    borderColor: COLORS.primary,
  },
  reorderText: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
    fontSize: 12,
  },
});
