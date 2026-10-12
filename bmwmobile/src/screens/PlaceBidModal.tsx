import React, { useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, TextInput, TouchableOpacity, ScrollView, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService, ServerError } from '../services/api';
import { currentUsername, isCurrentUser } from '../auth/session';
import { useCurrencyLabel, useMoney } from '../location/useMoney';
import { useLocations } from '../location/LocationContext';
import { REGIONS } from '../utils/geo';
import { useNextBid } from '../hooks/useNextBid';
import { NextBidCalculation } from '../components/NextBidCalculation';
import { Notice } from '../components/Notice';

type Props = NativeStackScreenProps<RootStackParamList, 'PlaceBidModal'>;

/**
 * Place a bid. The next valid bid, its calculation and the quick-bid amounts all come from BidService
 * (GET /bids/{id}/next-minimum) and update live when someone else bids. A bid is the TOTAL (part price + bid).
 */
export const PlaceBidModal: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const currencyLabel = useCurrencyLabel();
  const { country } = useLocations();
  const { auction } = route.params;
  const { next, loading, reload } = useNextBid(auction.id);
  const [bidAmount, setBidAmount] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const lastMinimum = useRef<number | null>(null);

  // Pre-fill the next valid bid; when a new bid raises it, move the box up too (never down).
  useEffect(() => {
    if (!next) return;
    const typed = parseInt(bidAmount, 10);
    if (!bidAmount || Number.isNaN(typed) || typed < next.minimumBid || typed === lastMinimum.current) {
      setBidAmount(String(next.minimumBid));
    }
    lastMinimum.current = next.minimumBid;
  }, [next?.minimumBid]); // eslint-disable-line react-hooks/exhaustive-deps

  const numericBid = parseInt(bidAmount, 10) || 0;
  const tooLow = !!next && numericBid < next.minimumBid;
  // The seller can't bid on their own listing; the highest bidder can't outbid themselves (the Bid service checks too).
  const blocked = isCurrentUser(auction.seller)
    ? "You can't bid on your own listing"
    : next?.highBidder && isCurrentUser(next.highBidder)
      ? "You're the highest bidder"
      : null;

  const handleConfirmBid = async () => {
    if (blocked) return;
    if (!next || tooLow) {
      if (next) setError(`The next valid bid is ${money(next.minimumBid)}`);
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await ApiService.placeBid(auction.id, numericBid);
      navigation.replace('HighestBidder', {
        auction: { ...auction, currentHighBid: numericBid, highBidder: currentUsername(), bidCount: next.bidCount + 1 },
      });
    } catch (err) {
      setError(err instanceof ServerError ? err.message : "Your bid couldn't be placed. Please try again.");
      reload();
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.headerTitle}>Place Bid</Text>
        <TouchableOpacity onPress={() => navigation.goBack()}>
          <Text style={styles.closeBtn}>✕</Text>
        </TouchableOpacity>
      </View>

      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Text style={styles.auctionTitle}>{auction.title}</Text>

        {!next ? (
          <View style={styles.currentSummary}>
            {loading ? (
              <ActivityIndicator color={COLORS.primaryGlow} />
            ) : (
              <>
                <Text style={styles.summaryLabel}>We couldn't load the current bid.</Text>
                <TouchableOpacity onPress={() => reload()}>
                  <Text style={styles.retry}>Try again</Text>
                </TouchableOpacity>
              </>
            )}
          </View>
        ) : (
          <>
            {/* Current total and the next valid bid */}
            <View style={styles.currentSummary}>
              <Text style={styles.summaryLabel}>{next.bidCount > 0 ? 'Current bid' : 'Part price'}</Text>
              <Text style={styles.summaryValue}>{money(next.currentTotal)}</Text>
              <Text style={styles.minIncrement}>
                {next.bidCount} {next.bidCount === 1 ? 'bid' : 'bids'} · Next bid {money(next.minimumBid)}
              </Text>
            </View>

            {/* Quick bids: next, next + 1, 2 and 5 steps */}
            <Text style={styles.label}>QUICK BID</Text>
            <View style={styles.quickRow}>
              {next.quickBids.map(val => (
                <TouchableOpacity
                  key={val}
                  style={[styles.quickChip, numericBid === val && styles.quickChipActive]}
                  onPress={() => {
                    setBidAmount(String(val));
                    setError(null);
                  }}
                >
                  <Text style={[styles.quickChipText, numericBid === val && styles.quickChipTextActive]} numberOfLines={1}>
                    {money(val)}
                  </Text>
                </TouchableOpacity>
              ))}
            </View>

            {/* Custom Bid Input (the total) */}
            <Text style={styles.label}>YOUR BID – TOTAL ({currencyLabel})</Text>
            <View style={[styles.inputWrap, tooLow && styles.inputWrapError]}>
              <Text style={styles.currencySymbol}>{REGIONS[country].currencySymbol}</Text>
              <TextInput
                style={styles.input}
                value={bidAmount}
                onChangeText={text => {
                  setBidAmount(text.replace(/[^0-9]/g, ''));
                  setError(null);
                }}
                keyboardType="number-pad"
                accessibilityLabel="Bid amount"
              />
            </View>

            {/* Current bid + step = next bid, and part price + your bid = total */}
            <NextBidCalculation next={next} bidTotal={numericBid || next.minimumBid} />

            <Text style={styles.howItWorks}>
              A bid is an amount on top of the part price. The highest total when the auction ends wins. Bids can't be withdrawn.
            </Text>
          </>
        )}

        {blocked ? <Notice tone="info" message={blocked} /> : null}
        {error ? <Notice tone="error" message={error} /> : null}

        <TouchableOpacity 
          style={[styles.confirmBtn, (!next || submitting || !!blocked) && styles.confirmBtnDisabled]} 
          onPress={handleConfirmBid} 
          disabled={!next || submitting || !!blocked}
          activeOpacity={0.8}
        >
          <Text style={styles.confirmText}>
            {submitting ? 'PLACING BID...' : `CONFIRM BID OF ${money(numericBid || next?.minimumBid || 0)}`}
          </Text>
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
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: SPACING.md,
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  headerTitle: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
  },
  closeBtn: {
    color: COLORS.textMuted,
    fontSize: 20,
    paddingHorizontal: SPACING.xs,
  },
  content: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  auctionTitle: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '700',
    marginBottom: SPACING.md,
  },
  currentSummary: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginBottom: SPACING.md,
  },
  summaryLabel: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  summaryValue: {
    color: COLORS.primaryGlow,
    fontSize: 26,
    fontWeight: '900',
    marginTop: 2,
  },
  minIncrement: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginTop: 4,
  },
  label: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  quickRow: {
    flexDirection: 'row',
    gap: SPACING.xs,
    marginBottom: SPACING.md,
  },
  quickChip: {
    flex: 1,
    backgroundColor: COLORS.card,
    paddingVertical: 10,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  quickChipActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  quickChipText: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontWeight: '700',
  },
  quickChipTextActive: {
    color: COLORS.text,
  },
  inputWrap: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.md,
    borderWidth: 2,
    borderColor: COLORS.primaryGlow,
    paddingHorizontal: SPACING.md,
    marginBottom: SPACING.md,
  },
  currencySymbol: {
    color: COLORS.primaryGlow,
    fontSize: 22,
    fontWeight: '900',
    marginRight: SPACING.xs,
  },
  input: {
    flex: 1,
    color: COLORS.text,
    fontSize: 22,
    fontWeight: '800',
    height: 50,
  },
  confirmBtn: {
    backgroundColor: COLORS.primary,
    marginTop: SPACING.lg,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  confirmBtnDisabled: {
    opacity: 0.6,
  },
  inputWrapError: {
    borderColor: COLORS.danger,
  },
  retry: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '800',
    marginTop: SPACING.xs,
  },
  howItWorks: {
    color: COLORS.textMuted,
    fontSize: 12,
    lineHeight: 17,
    marginTop: SPACING.md,
  },
  confirmText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
