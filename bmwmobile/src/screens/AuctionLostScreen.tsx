import React from 'react';
import { View, Text, StyleSheet, SafeAreaView, TouchableOpacity } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';

type Props = NativeStackScreenProps<RootStackParamList, 'AuctionLost'>;

export const AuctionLostScreen: React.FC<Props> = ({ route, navigation }) => {
  const { auction } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>📦</Text>
        </View>

        <Badge label="AUCTION ENDED — SOLD TO HIGHER BIDDER" variant="dark" />

        <Text style={styles.title}>{auction.title}</Text>

        <View style={styles.card}>
          <Text style={styles.desc}>
            This auction ended at <Text style={styles.bold}>${auction.currentHighBid.toLocaleString()}</Text>.
            Don't worry! You can submit a custom part order for the upcoming German container schedule with zero extra fee.
          </Text>
        </View>

        <TouchableOpacity 
          style={styles.requestBtn}
          onPress={() => navigation.navigate('OrderPartRequest', {
            prefillMake: auction.item.make,
            prefillModel: auction.item.model,
            prefillPart: auction.item.partName || auction.title
          })}
        >
          <Text style={styles.requestText}>SUBMIT CUSTOM ORDER FOR NEXT CONTAINER →</Text>
        </TouchableOpacity>

        <TouchableOpacity 
          style={styles.secondaryBtn}
          onPress={() => navigation.navigate('MainTabs', { screen: 'Marketplace' })}
        >
          <Text style={styles.secondaryText}>Back to Marketplace</Text>
        </TouchableOpacity>
      </View>
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
    justifyContent: 'center',
    flex: 1,
  },
  iconCircle: {
    width: 80,
    height: 80,
    borderRadius: 40,
    backgroundColor: COLORS.card,
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
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
  },
  card: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.xl,
  },
  desc: {
    color: COLORS.textSecondary,
    fontSize: 13,
    lineHeight: 20,
    textAlign: 'center',
  },
  bold: {
    color: COLORS.text,
    fontWeight: '700',
  },
  requestBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginBottom: SPACING.sm,
  },
  requestText: {
    color: COLORS.text,
    fontSize: 13,
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
    color: COLORS.textMuted,
    fontSize: 14,
    fontWeight: '600',
  },
});
