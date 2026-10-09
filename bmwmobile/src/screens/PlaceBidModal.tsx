import React, { useState } from 'react';
import { View, Text, StyleSheet, TextInput, TouchableOpacity, ScrollView, Switch } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService } from '../services/api';
import { currentUsername } from '../auth/session';
import { useCurrencyLabel, useMoney } from '../location/useMoney';

type Props = NativeStackScreenProps<RootStackParamList, 'PlaceBidModal'>;

export const PlaceBidModal: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const currencyLabel = useCurrencyLabel();
  const { auction } = route.params;
  const minBid = auction.currentHighBid + 250;
  const [bidAmount, setBidAmount] = useState(minBid.toString());
  const [isAutoBid, setIsAutoBid] = useState(false);
  const [maxAutoBid, setMaxAutoBid] = useState((minBid + 1500).toString());
  const [submitting, setSubmitting] = useState(false);

  const numericBid = parseFloat(bidAmount) || minBid;
  const buyerFee = numericBid * 0.03;

  const handleConfirmBid = async () => {
    setSubmitting(true);
    await ApiService.placeBid(auction.id, numericBid);
    if (isAutoBid) {
      await ApiService.setAutoBid(auction.id, parseFloat(maxAutoBid) || numericBid + 1000);
    }
    setSubmitting(false);
    navigation.replace('HighestBidder', { auction: { ...auction, currentHighBid: numericBid, highBidder: currentUsername() } });
  };

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.headerTitle}>Place Bid & Auto-Bid Setup</Text>
        <TouchableOpacity onPress={() => navigation.goBack()}>
          <Text style={styles.closeBtn}>✕</Text>
        </TouchableOpacity>
      </View>

      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.auctionTitle}>{auction.title}</Text>
        
        {/* Current High Bid summary */}
        <View style={styles.currentSummary}>
          <Text style={styles.summaryLabel}>Current Highest Bid</Text>
          <Text style={styles.summaryValue}>{money(auction.currentHighBid)}</Text>
          <Text style={styles.minIncrement}>Minimum Next Bid: {money(minBid)}</Text>
        </View>

        {/* Instant Increment Quick Chips */}
        <Text style={styles.label}>QUICK BID SELECTION</Text>
        <View style={styles.quickRow}>
          {[minBid, minBid + 250, minBid + 500, minBid + 1000].map(val => (
            <TouchableOpacity
              key={val}
              style={[styles.quickChip, parseFloat(bidAmount) === val && styles.quickChipActive]}
              onPress={() => setBidAmount(val.toString())}
            >
              <Text style={[styles.quickChipText, parseFloat(bidAmount) === val && styles.quickChipTextActive]}>
                {money(val)}
              </Text>
            </TouchableOpacity>
          ))}
        </View>

        {/* Custom Bid Input */}
        <Text style={styles.label}>YOUR BID AMOUNT ({currencyLabel})</Text>
        <View style={styles.inputWrap}>
          <Text style={styles.currencySymbol}>$</Text>
          <TextInput
            style={styles.input}
            value={bidAmount}
            onChangeText={setBidAmount}
            keyboardType="numeric"
          />
        </View>

        {/* Auto-Bid Protection Toggle */}
        <View style={styles.autoBidCard}>
          <View style={styles.autoBidHeader}>
            <View style={{ flex: 1 }}>
              <Text style={styles.autoBidTitle}>⚡ Enable Auto-Bid Ceiling Protection</Text>
              <Text style={styles.autoBidSub}>Automatically outbid competitors up to your maximum ceiling.</Text>
            </View>
            <Switch 
              value={isAutoBid} 
              onValueChange={setIsAutoBid} 
              trackColor={{ false: COLORS.border, true: COLORS.primary }}
            />
          </View>

          {isAutoBid && (
            <View style={styles.autoInputBox}>
              <Text style={styles.autoInputLabel}>MAX CEILING AUTO-BID AMOUNT ($)</Text>
              <TextInput
                style={styles.autoInput}
                value={maxAutoBid}
                onChangeText={setMaxAutoBid}
                keyboardType="numeric"
              />
            </View>
          )}
        </View>

        {/* Cost Breakdown */}
        <View style={styles.breakdown}>
          <View style={styles.breakRow}>
            <Text style={styles.breakKey}>Winning Bid Amount:</Text>
            <Text style={styles.breakVal}>{money(numericBid)}</Text>
          </View>
          <View style={styles.breakRow}>
            <Text style={styles.breakKey}>OKOH Buyer Clearing Fee (3%):</Text>
            <Text style={styles.breakVal}>{money(buyerFee, 2)}</Text>
          </View>
          <View style={styles.breakRow}>
            <Text style={styles.breakKey}>Est. Container Ocean Shipping:</Text>
            <Text style={styles.breakVal}>Included (Free Port Pickup)</Text>
          </View>
          <View style={[styles.breakRow, styles.totalRow]}>
            <Text style={styles.totalKey}>TOTAL ESTIMATED COST:</Text>
            <Text style={styles.totalVal}>{money((numericBid + buyerFee))}</Text>
          </View>
        </View>

        <TouchableOpacity 
          style={styles.confirmBtn} 
          onPress={handleConfirmBid} 
          disabled={submitting}
          activeOpacity={0.8}
        >
          <Text style={styles.confirmText}>
            {submitting ? 'CONFIRMING BID...' : `CONFIRM BID OF ${money(numericBid)}`}
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
  autoBidCard: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  autoBidHeader: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  autoBidTitle: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '700',
  },
  autoBidSub: {
    color: COLORS.textMuted,
    fontSize: 11,
    marginTop: 2,
  },
  autoInputBox: {
    marginTop: SPACING.md,
    paddingTop: SPACING.sm,
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
  },
  autoInputLabel: {
    color: COLORS.textSecondary,
    fontSize: 11,
    fontWeight: '700',
    marginBottom: 4,
  },
  autoInput: {
    backgroundColor: COLORS.card,
    color: COLORS.primaryGlow,
    fontSize: 18,
    fontWeight: '800',
    paddingHorizontal: SPACING.md,
    height: 44,
    borderRadius: RADIUS.sm,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  breakdown: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    marginBottom: SPACING.lg,
  },
  breakRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: 4,
  },
  breakKey: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  breakVal: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
  },
  totalRow: {
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
    marginTop: SPACING.xs,
    paddingTop: SPACING.sm,
  },
  totalKey: {
    color: COLORS.text,
    fontWeight: '800',
    fontSize: 13,
  },
  totalVal: {
    color: COLORS.primaryGlow,
    fontWeight: '900',
    fontSize: 16,
  },
  confirmBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  confirmText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
