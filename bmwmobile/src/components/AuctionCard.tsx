import React from 'react';
import { View, Text, Image, StyleSheet, TouchableOpacity } from 'react-native';
import { Auction } from '../types/models';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from './Badge';
import { isCurrentUser } from '../auth/session';
import { formatDistance } from '../utils/geo';

interface AuctionCardProps {
  auction: Auction;
  onPress: () => void;
  onBidPress?: () => void;
}

export const AuctionCard: React.FC<AuctionCardProps> = ({ auction, onPress, onBidPress }) => {
  const isWinning = isCurrentUser(auction.highBidder);
  const imageUrl = auction.item.images[0]?.url || 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop';

  return (
    <TouchableOpacity style={styles.card} onPress={onPress} activeOpacity={0.9}>
      {/* Image Container */}
      <View style={styles.imageWrap}>
        <Image source={{ uri: imageUrl }} style={styles.image} resizeMode="cover" />
        <View style={styles.badgeTopLeft}>
          <Badge 
            label={auction.status === 'EndingSoon' ? '🔥 Ending Soon' : 'LIVE AUCTION'} 
            variant={auction.status === 'EndingSoon' ? 'warning' : 'primary'}
            size="sm"
          />
        </View>
        {isWinning && (
          <View style={styles.badgeTopRight}>
            <Badge label="HIGHEST BIDDER" variant="success" size="sm" />
          </View>
        )}
      </View>

      {/* Content */}
      <View style={styles.content}>
        <Text style={styles.title} numberOfLines={2}>{auction.title}</Text>
        
        <View style={styles.metaRow}>
          <Text style={styles.metaPill}>Year: {auction.item.year}</Text>
          <Text style={styles.metaPill}>Seller: {auction.seller}</Text>
        </View>

        {/* Distance from the selected location */}
        {auction.distanceKm !== undefined || auction.locationName ? (
          <Text style={styles.distanceText} numberOfLines={1}>
            📍 {auction.distanceKm !== undefined ? <Text style={styles.distanceBold}>{formatDistance(auction.distanceKm)} away</Text> : null}
            {auction.distanceKm !== undefined && auction.locationName ? ' · ' : ''}
            {auction.locationName ?? ''}
          </Text>
        ) : null}

        {/* Pricing & Bids */}
        <View style={styles.priceContainer}>
          <View>
            <Text style={styles.priceLabel}>Current Highest Bid</Text>
            <Text style={styles.priceValue}>${auction.currentHighBid.toLocaleString()}</Text>
          </View>
          <View style={styles.bidCountBadge}>
            <Text style={styles.bidCountText}>{auction.bidCount} Bids</Text>
          </View>
        </View>

        {/* Port Logistics Strip */}
        {auction.pickupPort && (
          <View style={styles.logisticsStrip}>
            <Text style={styles.logisticsIcon}>🚢</Text>
            <Text style={styles.logisticsText}>
              Destination: <Text style={styles.logisticsBold}>{auction.pickupPort}</Text>
              {auction.containerNumber ? ` • Container ${auction.containerNumber}` : ''}
            </Text>
          </View>
        )}

        {/* Buttons */}
        <View style={styles.actionRow}>
          <TouchableOpacity style={styles.detailsBtn} onPress={onPress} activeOpacity={0.8}>
            <Text style={styles.detailsBtnText}>View Specs</Text>
          </TouchableOpacity>

          <TouchableOpacity style={styles.bidBtn} onPress={onBidPress || onPress} activeOpacity={0.8}>
            <Text style={styles.bidBtnText}>Place Bid</Text>
          </TouchableOpacity>
        </View>
      </View>
    </TouchableOpacity>
  );
};

const styles = StyleSheet.create({
  distanceText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginBottom: SPACING.sm,
  },
  distanceBold: {
    color: COLORS.primaryGlow,
    fontWeight: '800',
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
    overflow: 'hidden',
  },
  imageWrap: {
    height: 180,
    width: '100%',
    backgroundColor: COLORS.card,
    position: 'relative',
  },
  image: {
    width: '100%',
    height: '100%',
  },
  badgeTopLeft: {
    position: 'absolute',
    top: SPACING.sm,
    left: SPACING.sm,
  },
  badgeTopRight: {
    position: 'absolute',
    top: SPACING.sm,
    right: SPACING.sm,
  },
  content: {
    padding: SPACING.md,
  },
  title: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '700',
    lineHeight: 22,
  },
  metaRow: {
    flexDirection: 'row',
    marginTop: SPACING.xs,
    marginBottom: SPACING.sm,
    gap: SPACING.xs,
  },
  metaPill: {
    color: COLORS.textMuted,
    fontSize: 12,
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.sm,
    paddingVertical: 2,
    borderRadius: RADIUS.sm,
  },
  priceContainer: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginVertical: SPACING.xs,
  },
  priceLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    textTransform: 'uppercase',
    fontWeight: '600',
  },
  priceValue: {
    color: COLORS.primaryGlow,
    fontSize: 22,
    fontWeight: '800',
    marginTop: 2,
  },
  bidCountBadge: {
    backgroundColor: COLORS.badgeBg,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.primary,
  },
  bidCountText: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
    fontSize: 12,
  },
  logisticsStrip: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: SPACING.sm,
    backgroundColor: 'rgba(59, 130, 246, 0.1)',
    padding: SPACING.sm,
    borderRadius: RADIUS.sm,
  },
  logisticsIcon: {
    fontSize: 14,
    marginRight: 6,
  },
  logisticsText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    flex: 1,
  },
  logisticsBold: {
    color: COLORS.text,
    fontWeight: '700',
  },
  actionRow: {
    flexDirection: 'row',
    gap: SPACING.sm,
    marginTop: SPACING.md,
  },
  detailsBtn: {
    flex: 1,
    backgroundColor: COLORS.cardSecondary,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  detailsBtnText: {
    color: COLORS.text,
    fontWeight: '700',
    fontSize: 14,
  },
  bidBtn: {
    flex: 1,
    backgroundColor: COLORS.primary,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  bidBtnText: {
    color: COLORS.text,
    fontWeight: '800',
    fontSize: 14,
  },
});
