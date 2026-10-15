import React from 'react';
import { View, Text, Image, StyleSheet, TouchableOpacity } from 'react-native';
import { Auction } from '../types/models';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from './Badge';
import { Countdown } from './Countdown';
import { isCurrentUser } from '../auth/session';
import { formatDistance } from '../utils/geo';
import { useMoney } from '../location/useMoney';
import { useLocations } from '../location/LocationContext';
import { FEATURES } from '../config';
import { label } from '../utils/time';
import { deliverySummary } from '../utils/delivery';
import { ratingText } from '../utils/rating';

/** The seller's name, or their username on older listings. */
export const sellerLabel = (a: Pick<Auction, 'seller' | 'sellerName'>) => a.sellerName || a.seller;

interface AuctionCardProps {
  auction: Auction;
  onPress: () => void;
  onBidPress?: () => void;
  /** Tapping the seller's name: show everything this seller lists. Without it the name is plain text. */
  onSellerPress?: () => void;
  /** Two-column grid card (smaller, no buttons). */
  compact?: boolean;
}

/**
 * A listing like an online shop: cover photo, title, fitment summary, condition, price in the local currency, the
 * seller (tap: everything they sell) and distance. Auctions show the current bid, bids and a live countdown;
 * fixed-price listings show the price and how many are left. No container / port information.
 */
export const AuctionCard: React.FC<AuctionCardProps> = ({ auction, onPress, onBidPress, onSellerPress, compact }) => {
  const money = useMoney();
  const { country } = useLocations();
  const isFixed = auction.saleType === 'FixedPrice';
  const isWinning = !isFixed && isCurrentUser(auction.highBidder);
  const isMine = isCurrentUser(auction.seller);
  const imageUrl = auction.thumbnailUrl || auction.item.images[0]?.url;
  const hasBids = auction.currentHighBid > auction.startingPrice;
  const ending = !isFixed && new Date(auction.auctionEnd).getTime() - Date.now() < 6 * 3600 * 1000;
  const left = auction.quantityAvailable ?? 1;
  const delivery = deliverySummary(auction, money);
  const condition = [label(auction.item.condition), auction.usedGrade ? `Grade ${auction.usedGrade}` : null].filter(Boolean).join(' · ');
  const fitment = auction.fitmentSummary ?? (auction.item.make ? `Fits ${auction.item.make} ${auction.item.model}` : undefined);

  return (
    <TouchableOpacity style={[styles.card, compact && styles.cardCompact]} onPress={onPress} activeOpacity={0.9}>
      <View style={[styles.imageWrap, compact && styles.imageWrapCompact]}>
        {imageUrl ? (
          <Image source={{ uri: imageUrl }} style={styles.image} resizeMode="cover" />
        ) : (
          <View style={styles.noImage}><Text style={styles.noImageText}>📷</Text></View>
        )}
        <View style={styles.badgeTopLeft}>
          {isFixed ? (
            <Badge label="BUY NOW" variant="success" size="sm" />
          ) : (
            <Badge label={ending ? '🔥 Ending soon' : 'LIVE'} variant={ending ? 'warning' : 'primary'} size="sm" />
          )}
        </View>
        {isWinning ? (
          <View style={styles.badgeTopRight}>
            <Badge label="HIGHEST BIDDER" variant="success" size="sm" />
          </View>
        ) : null}
      </View>

      <View style={[styles.content, compact && styles.contentCompact]}>
        {auction.badge ? (
          <Text style={[styles.matchBadge, auction.badge === 'Check fitment' && styles.matchBadgeWarn]} numberOfLines={1}>
            {auction.badge === 'Check fitment' ? '⚠️ ' : '✓ '}{auction.badge}
          </Text>
        ) : null}
        <Text style={[styles.title, compact && styles.titleCompact]} numberOfLines={2}>{auction.title}</Text>
        {fitment ? <Text style={styles.fitment} numberOfLines={1}>{fitment}</Text> : null}
        {condition && !compact ? <Text style={styles.meta} numberOfLines={1}>{condition}</Text> : null}

        <View style={styles.priceRow}>
          <View style={styles.priceCol}>
            <Text style={styles.priceLabel}>{isFixed ? 'Price' : hasBids ? 'Current bid' : 'Part price'}</Text>
            <Text style={[styles.priceValue, compact && styles.priceCompact]}>
              {money(isFixed ? auction.startingPrice : auction.currentHighBid)}
            </Text>
          </View>
          <View style={styles.rightCol}>
            {isFixed ? (
              <Text style={styles.small}>{left > 1 ? `${left} available` : 'Direct sale'}</Text>
            ) : (
              <>
                <Text style={styles.small}>{auction.bidCount} {auction.bidCount === 1 ? 'bid' : 'bids'}</Text>
                <Countdown end={auction.auctionEnd} prefix="⏱ " style={[styles.small, styles.timer, ending && styles.ending]} />
              </>
            )}
          </View>
        </View>

        {/* The seller: tap to see everything they sell. */}
        <TouchableOpacity
          onPress={onSellerPress}
          disabled={!onSellerPress}
          hitSlop={6}
          accessibilityRole="link"
          accessibilityLabel={`More from ${sellerLabel(auction)}, rated ${ratingText(auction.sellerRating, auction.sellerRatingCount)}`}
        >
          <Text style={styles.sellerText} numberOfLines={1}>
            {compact ? '' : 'Seller: '}
            <Text style={[styles.sellerName, !!onSellerPress && styles.sellerLink]}>{isMine ? 'You' : sellerLabel(auction)}</Text>
          </Text>
          {/* The seller's rating out of 5 and how many people rated. */}
          <Text style={styles.rating} numberOfLines={1}>{ratingText(auction.sellerRating, auction.sellerRatingCount, compact)}</Text>
        </TouchableOpacity>

        {auction.distanceKm !== undefined || auction.locationName ? (
          <Text style={styles.distanceText} numberOfLines={1}>
            📍 {auction.distanceKm !== undefined ? <Text style={styles.distanceBold}>{formatDistance(auction.distanceKm, country)} away</Text> : null}
            {auction.distanceKm !== undefined && auction.locationName && !compact ? ' · ' : ''}
            {compact ? '' : auction.locationName ?? ''}
          </Text>
        ) : null}

        {/* What the buyer can get: "📍 Pickup in Hyderabad · 🚚 Shipping ₹500" */}
        {delivery ? <Text style={styles.deliveryText} numberOfLines={1}>{delivery}</Text> : null}

        {/* Port Logistics Strip (hidden while FEATURES.containers is off) */}
        {FEATURES.containers && auction.pickupPort && (
          <View style={styles.logisticsStrip}>
            <Text style={styles.logisticsText}>🚢 Destination: {auction.pickupPort}</Text>
          </View>
        )}

        {!compact ? (
          <View style={styles.actionRow}>
            <TouchableOpacity style={styles.detailsBtn} onPress={onPress} activeOpacity={0.8}>
              <Text style={styles.detailsBtnText}>View Specs</Text>
            </TouchableOpacity>
            {/* The seller can't bid on (or buy) their own listing. Buying happens on the listing page (quantity). */}
            {!isMine ? (
              <TouchableOpacity style={[styles.bidBtn, isFixed && styles.buyBtn]} onPress={isFixed ? onPress : onBidPress || onPress} activeOpacity={0.8}>
                <Text style={styles.bidBtnText} numberOfLines={1}>
                  {isFixed ? `Buy now – ${money(auction.startingPrice)}` : isWinning ? "You're winning" : 'Place Bid'}
                </Text>
              </TouchableOpacity>
            ) : null}
          </View>
        ) : null}
      </View>
    </TouchableOpacity>
  );
};

const styles = StyleSheet.create({
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
    overflow: 'hidden',
  },
  cardCompact: {
    flex: 1,
    marginHorizontal: SPACING.xs,
  },
  imageWrap: {
    height: 180,
    width: '100%',
    backgroundColor: COLORS.card,
  },
  imageWrapCompact: {
    height: 120,
  },
  image: {
    width: '100%',
    height: '100%',
  },
  noImage: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  noImageText: {
    fontSize: 32,
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
  contentCompact: {
    padding: SPACING.sm,
  },
  matchBadge: {
    color: COLORS.successGlow,
    fontSize: 11,
    fontWeight: '800',
    marginBottom: 4,
  },
  matchBadgeWarn: {
    color: COLORS.warningGlow,
  },
  title: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '700',
    lineHeight: 22,
  },
  titleCompact: {
    fontSize: 13,
    lineHeight: 17,
  },
  fitment: {
    color: COLORS.badgeText,
    fontSize: 12,
    fontWeight: '600',
    marginTop: 4,
  },
  meta: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  priceRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-end',
    marginTop: SPACING.sm,
  },
  priceCol: {
    flexShrink: 1,
  },
  rightCol: {
    alignItems: 'flex-end',
  },
  priceLabel: {
    color: COLORS.textMuted,
    fontSize: 10,
    textTransform: 'uppercase',
    fontWeight: '600',
  },
  priceValue: {
    color: COLORS.primaryGlow,
    fontSize: 20,
    fontWeight: '800',
  },
  priceCompact: {
    fontSize: 15,
  },
  small: {
    color: COLORS.textSecondary,
    fontSize: 11,
    fontWeight: '600',
  },
  ending: {
    color: COLORS.warningGlow,
  },
  timer: {
    fontVariant: ['tabular-nums'],
  },
  sellerText: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: SPACING.xs,
  },
  sellerName: {
    color: COLORS.textSecondary,
    fontWeight: '700',
  },
  rating: {
    color: COLORS.warningGlow,
    fontSize: 12,
    fontWeight: '700',
    marginTop: 2,
  },
  sellerLink: {
    color: COLORS.primaryGlow,
    textDecorationLine: 'underline',
  },
  distanceText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginTop: SPACING.xs,
  },
  deliveryText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
    marginTop: 2,
  },
  distanceBold: {
    color: COLORS.primaryGlow,
    fontWeight: '800',
  },
  logisticsStrip: {
    marginTop: SPACING.sm,
    backgroundColor: 'rgba(59, 130, 246, 0.1)',
    padding: SPACING.sm,
    borderRadius: RADIUS.sm,
  },
  logisticsText: {
    color: COLORS.textSecondary,
    fontSize: 12,
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
  buyBtn: {
    backgroundColor: COLORS.success,
  },
  bidBtnText: {
    color: COLORS.text,
    fontWeight: '800',
    fontSize: 14,
  },
});
