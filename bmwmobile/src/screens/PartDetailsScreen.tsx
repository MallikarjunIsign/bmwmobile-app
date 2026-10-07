import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, ScrollView, Image, TouchableOpacity, SafeAreaView } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Badge } from '../components/Badge';
import { ApiService } from '../services/api';
import { Auction } from '../types/models';

type Props = NativeStackScreenProps<RootStackParamList, 'PartDetails'>;

export const PartDetailsScreen: React.FC<Props> = ({ route, navigation }) => {
  const { auctionId, auction: initialAuction } = route.params;
  const [auction, setAuction] = useState<Auction | undefined>(initialAuction);

  useEffect(() => {
    if (!auction) {
      ApiService.getAuctionById(auctionId).then(setAuction);
    }
  }, [auctionId]);

  if (!auction) return null;

  const isWinning = auction.highBidder === 'demo_buyer';
  const isEnded = auction.status === 'Ended' || auction.status === 'Won' || auction.status === 'Lost';

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Part Specifications" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* Main Part Image */}
        <View style={styles.imageBox}>
          <Image 
            source={{ uri: auction.item.images[0]?.url || 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop' }} 
            style={styles.image} 
            resizeMode="cover"
          />
          <View style={styles.badgeWrap}>
            <Badge label={auction.status} variant={auction.status === 'EndingSoon' ? 'warning' : 'primary'} />
          </View>
        </View>

        {/* Title & Description */}
        <View style={styles.card}>
          <Text style={styles.title}>{auction.title}</Text>
          <Text style={styles.seller}>Listed by: <Text style={styles.sellerBold}>{auction.seller}</Text></Text>

          {/* Price Strip */}
          <View style={styles.priceStrip}>
            <View>
              <Text style={styles.priceLabel}>Current Highest Bid</Text>
              <Text style={styles.priceValue}>${auction.currentHighBid.toLocaleString()}</Text>
            </View>
            <View style={styles.bidBadge}>
              <Text style={styles.bidText}>{auction.bidCount} Bids Placed</Text>
            </View>
          </View>

          {/* Status Alert Banner */}
          {isWinning ? (
            <View style={styles.winningBanner}>
              <Text style={styles.winningText}>✓ You are currently the HIGHEST BIDDER</Text>
              {auction.isAutoBidActive && (
                <Text style={styles.autoBidText}>⚡ Auto-Bid Protection Active up to ${auction.userMaxAutoBid?.toLocaleString()}</Text>
              )}
            </View>
          ) : null}
        </View>

        {/* Logistics & Container Feasibility */}
        <View style={styles.card}>
          <Text style={styles.sectionHeading}>🚢 CONTAINER LOGISTICS & PORT DESTINATION</Text>
          <View style={styles.infoRow}>
            <Text style={styles.infoKey}>Assigned Container:</Text>
            <Text style={styles.infoValue}>{auction.containerNumber || 'CN-2026-09'}</Text>
          </View>
          <View style={styles.infoRow}>
            <Text style={styles.infoKey}>Nigerian Clearing Port:</Text>
            <Text style={styles.infoValue}>{auction.pickupPort || 'Lagos Apapa Port'}</Text>
          </View>
          <View style={styles.infoRow}>
            <Text style={styles.infoKey}>Est. Port Arrival:</Text>
            <Text style={styles.infoValue}>{auction.estimatedArrival || 'Nov 02, 2026'}</Text>
          </View>
        </View>

        {/* Part Specs & Compatibility */}
        <View style={styles.card}>
          <Text style={styles.sectionHeading}>⚙️ TECHNICAL SPECIFICATIONS & FITMENT</Text>
          <View style={styles.infoRow}>
            <Text style={styles.infoKey}>BMW Part Number:</Text>
            <Text style={styles.infoValue}>{auction.item.partNumber || '11002472910'}</Text>
          </View>
          <View style={styles.infoRow}>
            <Text style={styles.infoKey}>Fits Vehicle Model:</Text>
            <Text style={styles.infoValue}>{auction.item.year} {auction.item.make} {auction.item.model}</Text>
          </View>
          <View style={styles.infoRow}>
            <Text style={styles.infoKey}>Part Condition:</Text>
            <Text style={styles.infoValue}>{auction.item.condition || 'Grade A OEM'}</Text>
          </View>

          <Text style={styles.descTitle}>Description:</Text>
          <Text style={styles.descText}>{auction.item.description}</Text>
        </View>
      </ScrollView>

      {/* Fixed Bottom Action Bar */}
      <View style={styles.bottomBar}>
        {isEnded ? (
          <TouchableOpacity 
            style={styles.requestAltBtn} 
            onPress={() => navigation.navigate('OrderPartRequest', {
              prefillMake: auction.item.make,
              prefillModel: auction.item.model,
              prefillPart: auction.item.partName || auction.title
            })}
          >
            <Text style={styles.requestAltText}>AUCTION ENDED — ORDER CUSTOM PART FOR NEXT CONTAINER</Text>
          </TouchableOpacity>
        ) : (
          <TouchableOpacity 
            style={styles.bidBarBtn} 
            onPress={() => navigation.navigate('PlaceBidModal', { auction })}
          >
            <Text style={styles.bidBarText}>PLACE BID NOW (${(auction.currentHighBid + 250).toLocaleString()})</Text>
          </TouchableOpacity>
        )}
      </View>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  scroll: {
    padding: SPACING.md,
    paddingBottom: 90,
  },
  imageBox: {
    height: 220,
    width: '100%',
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.lg,
    overflow: 'hidden',
    position: 'relative',
    marginBottom: SPACING.md,
  },
  image: {
    width: '100%',
    height: '100%',
  },
  badgeWrap: {
    position: 'absolute',
    top: SPACING.md,
    left: SPACING.md,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    padding: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    lineHeight: 24,
  },
  seller: {
    color: COLORS.textMuted,
    fontSize: 13,
    marginTop: 4,
    marginBottom: SPACING.md,
  },
  sellerBold: {
    color: COLORS.textSecondary,
    fontWeight: '700',
  },
  priceStrip: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  priceLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    textTransform: 'uppercase',
  },
  priceValue: {
    color: COLORS.primaryGlow,
    fontSize: 24,
    fontWeight: '900',
    marginTop: 2,
  },
  bidBadge: {
    backgroundColor: COLORS.badgeBg,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.primary,
  },
  bidText: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
    fontSize: 12,
  },
  winningBanner: {
    backgroundColor: 'rgba(34, 164, 71, 0.15)',
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.success,
    marginTop: SPACING.md,
  },
  winningText: {
    color: COLORS.success,
    fontWeight: '800',
    fontSize: 13,
  },
  autoBidText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginTop: 4,
  },
  sectionHeading: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.sm,
  },
  infoRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: 6,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  infoKey: {
    color: COLORS.textMuted,
    fontSize: 13,
  },
  infoValue: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '700',
  },
  descTitle: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontWeight: '700',
    marginTop: SPACING.md,
    marginBottom: 4,
  },
  descText: {
    color: COLORS.textMuted,
    fontSize: 13,
    lineHeight: 18,
  },
  bottomBar: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
  },
  bidBarBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  bidBarText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '900',
    letterSpacing: 1,
  },
  requestAltBtn: {
    backgroundColor: COLORS.cardSecondary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.primary,
  },
  requestAltText: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
});
