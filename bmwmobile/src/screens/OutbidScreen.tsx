import React from 'react';
import { View, Text, StyleSheet, SafeAreaView, TouchableOpacity, ScrollView } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';

type Props = NativeStackScreenProps<RootStackParamList, 'Outbid'>;

export const OutbidScreen: React.FC<Props> = ({ route, navigation }) => {
  const { auction } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>⚠️</Text>
        </View>

        <Badge label="YOU WERE OUTBID" variant="danger" />

        <Text style={styles.title}>{auction.title}</Text>

        <View style={styles.card}>
          <Text style={styles.cardLabel}>NEW HIGHEST BID IN MARKETPLACE</Text>
          <Text style={styles.bidAmount}>${auction.currentHighBid.toLocaleString()}</Text>
          <Text style={styles.bidderText}>Bid by: <Text style={styles.boldText}>autoworks_lagos</Text></Text>

          <View style={styles.row}>
            <Text style={styles.key}>Your Previous Highest Bid:</Text>
            <Text style={styles.val}>${(auction.currentHighBid - 200).toLocaleString()}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Suggested Counter Bid:</Text>
            <Text style={styles.valCounter}>${(auction.currentHighBid + 250).toLocaleString()}</Text>
          </View>
        </View>

        <TouchableOpacity 
          style={styles.counterBtn}
          onPress={() => navigation.navigate('PlaceBidModal', { auction })}
          activeOpacity={0.8}
        >
          <Text style={styles.counterText}>PLACE COUNTER BID NOW (${(auction.currentHighBid + 250).toLocaleString()})</Text>
        </TouchableOpacity>

        <TouchableOpacity 
          style={styles.secondaryBtn}
          onPress={() => navigation.goBack()}
          activeOpacity={0.8}
        >
          <Text style={styles.secondaryText}>Back to Bids</Text>
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
    backgroundColor: 'rgba(239, 68, 68, 0.15)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 2,
    borderColor: COLORS.danger,
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
    borderColor: COLORS.danger,
    marginBottom: SPACING.lg,
  },
  cardLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    textAlign: 'center',
  },
  bidAmount: {
    color: COLORS.danger,
    fontSize: 32,
    fontWeight: '900',
    textAlign: 'center',
    marginTop: 4,
  },
  bidderText: {
    color: COLORS.textMuted,
    fontSize: 12,
    textAlign: 'center',
    marginBottom: SPACING.md,
  },
  boldText: {
    color: COLORS.text,
    fontWeight: '700',
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
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
  },
  valCounter: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '900',
  },
  counterBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginBottom: SPACING.sm,
  },
  counterText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
  secondaryBtn: {
    width: '100%',
    backgroundColor: COLORS.card,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  secondaryText: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
  },
});
