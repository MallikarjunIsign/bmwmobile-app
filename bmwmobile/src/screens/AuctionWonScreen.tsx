import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';
import { useMoney } from '../location/useMoney';

type Props = NativeStackScreenProps<RootStackParamList, 'AuctionWon'>;

export const AuctionWonScreen: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const { auction } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>🎉</Text>
        </View>

        <Badge label="VICTORY — AUCTION WON" variant="success" />

        <Text style={styles.title}>{auction.title}</Text>

        <View style={styles.card}>
          <Text style={styles.label}>WINNING BID TOTAL</Text>
          <Text style={styles.amount}>{money(auction.currentHighBid)}</Text>

          <View style={styles.row}>
            <Text style={styles.key}>Assigned Container:</Text>
            <Text style={styles.val}>{auction.containerNumber || 'MSCU7821940'}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Nigerian Clearing Port:</Text>
            <Text style={styles.val}>{auction.pickupPort || 'Lagos Apapa Port'}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Est. Port Arrival Date:</Text>
            <Text style={styles.val}>{auction.estimatedArrival || 'Nov 02, 2026'}</Text>
          </View>
        </View>

        <TouchableOpacity 
          style={styles.checkoutBtn}
          onPress={() => navigation.navigate('CompletePurchase', { auctionId: auction.id, auction })}
          activeOpacity={0.8}
        >
          <Text style={styles.checkoutText}>PROCEED TO CHECKOUT & GENERATE PICKUP PASS →</Text>
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
    width: 90,
    height: 90,
    borderRadius: 45,
    backgroundColor: 'rgba(34, 164, 71, 0.2)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 3,
    borderColor: COLORS.success,
    marginBottom: SPACING.md,
    marginTop: SPACING.lg,
  },
  iconText: {
    fontSize: 44,
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    textAlign: 'center',
    marginVertical: SPACING.md,
  },
  card: {
    width: '100%',
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.success,
    marginBottom: SPACING.xl,
  },
  label: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    textAlign: 'center',
  },
  amount: {
    color: COLORS.success,
    fontSize: 34,
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
  checkoutBtn: {
    width: '100%',
    backgroundColor: COLORS.success,
    paddingVertical: 16,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  checkoutText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
});
