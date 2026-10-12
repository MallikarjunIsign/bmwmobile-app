import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { COLORS, RADIUS, SPACING } from '../theme/colors';
import { useMoney } from '../location/useMoney';
import type { NextBid } from '../types/models';

interface NextBidCalculationProps {
  next: NextBid;
  /** The amount being bid (defaults to the next valid bid): adds "Part price + your bid = Total". */
  bidTotal?: number;
}

/**
 * The visible calculation from BidService:
 *   "Current bid $1,000 + step $20 = Next bid $1,020"
 *   "Part price $300 + your bid $50 = Total $350"
 */
export const NextBidCalculation: React.FC<NextBidCalculationProps> = ({ next, bidTotal }) => {
  const money = useMoney();
  const total = bidTotal ?? next.minimumBid;
  const currentLabel = next.bidCount > 0 ? 'Current bid' : 'Part price';

  return (
    <View style={styles.box}>
      <Text style={styles.line} accessibilityLabel="next-bid-calculation">
        {currentLabel} <Text style={styles.bold}>{money(next.currentTotal)}</Text> + step{' '}
        <Text style={styles.bold}>{money(next.bidStep)}</Text> = Next bid{' '}
        <Text style={styles.accent}>{money(next.minimumBid)}</Text>
      </Text>
      <Text style={styles.subLine}>
        Part price <Text style={styles.bold}>{money(next.partPrice)}</Text> + your bid{' '}
        <Text style={styles.bold}>{money(Math.max(total - next.partPrice, 0))}</Text> = Total{' '}
        <Text style={styles.bold}>{money(total)}</Text>
      </Text>
    </View>
  );
};

const styles = StyleSheet.create({
  box: {
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    padding: SPACING.md - 4,
    marginTop: SPACING.sm,
    gap: 4,
  },
  line: {
    color: COLORS.textSecondary,
    fontSize: 13,
  },
  subLine: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  bold: {
    color: COLORS.text,
    fontWeight: '800',
  },
  accent: {
    color: COLORS.primaryGlow,
    fontWeight: '900',
  },
});
