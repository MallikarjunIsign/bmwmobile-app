import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';

type Props = NativeStackScreenProps<RootStackParamList, 'PaymentConfirmed'>;

export const PaymentConfirmedScreen: React.FC<Props> = ({ route, navigation }) => {
  const { purchase } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>✓</Text>
        </View>

        <Badge label="PAYMENT CONFIRMED — ORDER PAID" variant="success" />

        <Text style={styles.title}>{purchase.itemTitle}</Text>

        {/* QR Code Pass Card */}
        <View style={styles.qrCard}>
          <Text style={styles.qrHeader}>CUSTOMS PORT PICKUP PASS CODE</Text>
          <View style={styles.qrBox}>
            <Text style={styles.qrCodeText}>{purchase.qrPassCode}</Text>
            <Text style={styles.qrInstruction}>Present this pass at {purchase.pickupPort} warehouse</Text>
          </View>
        </View>

        {/* Order Details */}
        <View style={styles.detailsCard}>
          <View style={styles.row}>
            <Text style={styles.key}>Order ID:</Text>
            <Text style={styles.val}>{purchase.id}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Total Paid:</Text>
            <Text style={styles.valSuccess}>${purchase.totalAmount.toLocaleString()}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Assigned Container:</Text>
            <Text style={styles.val}>{purchase.containerNumber || 'CN-2026-09'}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Pickup Port Location:</Text>
            <Text style={styles.val}>{purchase.pickupPort}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Receiving Method:</Text>
            <Text style={styles.val}>{purchase.deliveryOption === 'PortPickup' ? 'Free Port Warehouse Pickup' : 'Doorstep Dispatch'}</Text>
          </View>
        </View>

        <TouchableOpacity 
          style={styles.trackBtn}
          onPress={() => navigation.navigate('PickupReceive', { purchase })}
          activeOpacity={0.8}
        >
          <Text style={styles.trackText}>VIEW FULL QR PASS & PICKUP STATUS →</Text>
        </TouchableOpacity>

        <TouchableOpacity 
          style={styles.secondaryBtn}
          onPress={() => navigation.navigate('MainTabs', { screen: 'MyPurchases' }, { pop: true })}
        >
          <Text style={styles.secondaryText}>Go to My Purchases</Text>
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
    backgroundColor: 'rgba(34, 164, 71, 0.15)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 2,
    borderColor: COLORS.success,
    marginBottom: SPACING.md,
    marginTop: SPACING.md,
  },
  iconText: {
    fontSize: 36,
    color: COLORS.success,
    fontWeight: 'bold',
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    textAlign: 'center',
    marginVertical: SPACING.md,
  },
  qrCard: {
    width: '100%',
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    alignItems: 'center',
    marginBottom: SPACING.md,
  },
  qrHeader: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  qrBox: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    width: '100%',
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  qrCodeText: {
    color: COLORS.text,
    fontSize: 26,
    fontWeight: '900',
    letterSpacing: 3,
  },
  qrInstruction: {
    color: COLORS.textMuted,
    fontSize: 11,
    marginTop: 4,
  },
  detailsCard: {
    width: '100%',
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.xl,
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
  valSuccess: {
    color: COLORS.success,
    fontSize: 13,
    fontWeight: '800',
  },
  trackBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginBottom: SPACING.sm,
  },
  trackText: {
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
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
  },
});
