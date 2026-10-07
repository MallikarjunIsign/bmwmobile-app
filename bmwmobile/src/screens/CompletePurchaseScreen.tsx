import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, TextInput } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { ApiService, MOCK_AUCTIONS } from '../services/api';

type Props = NativeStackScreenProps<RootStackParamList, 'CompletePurchase'>;

export const CompletePurchaseScreen: React.FC<Props> = ({ route, navigation }) => {
  const { auctionId, auction: initialAuction } = route.params;
  const auction = initialAuction || MOCK_AUCTIONS[0];

  const [deliveryOption, setDeliveryOption] = useState<'PortPickup' | 'DoorstepDelivery'>('PortPickup');
  const [pickupPort, setPickupPort] = useState('Lagos Apapa Port');
  const [deliveryAddress, setDeliveryAddress] = useState('14 Ikeja Industrial Estate, Lagos, Nigeria');
  const [paymentMethod, setPaymentMethod] = useState<'BankTransfer' | 'Card'>('BankTransfer');
  const [submitting, setSubmitting] = useState(false);

  const winningBid = auction.currentHighBid || 14500;
  const buyerFee = winningBid * 0.03;
  const shippingFee = deliveryOption === 'DoorstepDelivery' ? 45 : 0;
  const totalCost = winningBid + buyerFee + shippingFee;

  const handleCheckout = async () => {
    setSubmitting(true);
    const purchase = await ApiService.checkout({
      auctionId: auction.id,
      deliveryOption,
      deliveryAddress: deliveryOption === 'DoorstepDelivery' ? deliveryAddress : undefined,
      pickupPort,
      paymentMethod,
    });
    setSubmitting(false);
    navigation.replace('PaymentConfirmed', { purchase });
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Complete Purchase" subtitle="Select port pickup or doorstep delivery" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* Item Summary */}
        <View style={styles.card}>
          <Text style={styles.itemTitle}>{auction.title}</Text>

          <View style={styles.row}>
            <Text style={styles.key}>Winning Auction Bid:</Text>
            <Text style={styles.val}>${winningBid.toLocaleString()}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>OKOH Clearing Fee (3%):</Text>
            <Text style={styles.val}>${buyerFee.toFixed(2)}</Text>
          </View>
        </View>

        {/* Delivery Selection */}
        <Text style={styles.sectionTitle}>1. CHOOSE RECEIVING OPTION</Text>

        <TouchableOpacity 
          style={[styles.optionCard, deliveryOption === 'PortPickup' && styles.optionCardSelected]}
          onPress={() => setDeliveryOption('PortPickup')}
          activeOpacity={0.8}
        >
          <View style={styles.radio}>
            {deliveryOption === 'PortPickup' && <View style={styles.radioInner} />}
          </View>
          <View style={{ flex: 1 }}>
            <View style={styles.optionHeader}>
              <Text style={styles.optionTitle}>Free Port Pickup Pass</Text>
              <Text style={styles.freeBadge}>FREE ($0)</Text>
            </View>
            <Text style={styles.optionSub}>Pick up directly at customs warehouse in {pickupPort} with QR pass.</Text>
          </View>
        </TouchableOpacity>

        <TouchableOpacity 
          style={[styles.optionCard, deliveryOption === 'DoorstepDelivery' && styles.optionCardSelected]}
          onPress={() => setDeliveryOption('DoorstepDelivery')}
          activeOpacity={0.8}
        >
          <View style={styles.radio}>
            {deliveryOption === 'DoorstepDelivery' && <View style={styles.radioInner} />}
          </View>
          <View style={{ flex: 1 }}>
            <View style={styles.optionHeader}>
              <Text style={styles.optionTitle}>Doorstep Dispatch Delivery</Text>
              <Text style={styles.priceTag}>+$45.00</Text>
            </View>
            <Text style={styles.optionSub}>Flat rate dispatch to your garage or workshop in Nigeria.</Text>
          </View>
        </TouchableOpacity>

        {deliveryOption === 'DoorstepDelivery' && (
          <View style={styles.addressBox}>
            <Text style={styles.addressLabel}>DELIVERY ADDRESS IN NIGERIA</Text>
            <TextInput
              style={styles.addressInput}
              value={deliveryAddress}
              onChangeText={setDeliveryAddress}
              multiline
            />
          </View>
        )}

        {/* Payment Method */}
        <Text style={styles.sectionTitle}>2. CHOOSE PAYMENT METHOD</Text>

        <TouchableOpacity 
          style={[styles.optionCard, paymentMethod === 'BankTransfer' && styles.optionCardSelected]}
          onPress={() => setPaymentMethod('BankTransfer')}
        >
          <View style={styles.radio}>
            {paymentMethod === 'BankTransfer' && <View style={styles.radioInner} />}
          </View>
          <View style={{ flex: 1 }}>
            <Text style={styles.optionTitle}>Bank Transfer (GTBank / Zenith)</Text>
            <Text style={styles.optionSub}>Zero processing fees. Instant confirmation via GTB/Zenith NGN/USD.</Text>
          </View>
        </TouchableOpacity>

        <TouchableOpacity 
          style={[styles.optionCard, paymentMethod === 'Card' && styles.optionCardSelected]}
          onPress={() => setPaymentMethod('Card')}
        >
          <View style={styles.radio}>
            {paymentMethod === 'Card' && <View style={styles.radioInner} />}
          </View>
          <View style={{ flex: 1 }}>
            <Text style={styles.optionTitle}>Debit / Credit Card</Text>
            <Text style={styles.optionSub}>Visa, MasterCard, Verve via Paystack / Flutterwave.</Text>
          </View>
        </TouchableOpacity>

        {/* Total Cost Summary */}
        <View style={styles.totalCard}>
          <View style={styles.totalRow}>
            <Text style={styles.totalLabel}>TOTAL AMOUNT DUE:</Text>
            <Text style={styles.totalValue}>${totalCost.toLocaleString()}</Text>
          </View>
        </View>

        <TouchableOpacity 
          style={styles.payBtn}
          onPress={handleCheckout}
          disabled={submitting}
          activeOpacity={0.8}
        >
          <Text style={styles.payText}>
            {submitting ? 'PROCESSING ORDER...' : `CONFIRM PAYMENT ($${totalCost.toLocaleString()})`}
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
  scroll: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  card: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  itemTitle: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
    marginBottom: SPACING.sm,
  },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: 4,
  },
  key: {
    color: COLORS.textMuted,
    fontSize: 13,
  },
  val: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '700',
  },
  sectionTitle: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1,
    marginTop: SPACING.md,
    marginBottom: SPACING.xs,
  },
  optionCard: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.xs,
  },
  optionCardSelected: {
    borderColor: COLORS.primaryGlow,
    backgroundColor: COLORS.card,
  },
  radio: {
    width: 20,
    height: 20,
    borderRadius: 10,
    borderWidth: 2,
    borderColor: COLORS.borderHighlight,
    justifyContent: 'center',
    alignItems: 'center',
    marginRight: SPACING.md,
  },
  radioInner: {
    width: 10,
    height: 10,
    borderRadius: 5,
    backgroundColor: COLORS.primaryGlow,
  },
  optionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  optionTitle: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '700',
  },
  freeBadge: {
    color: COLORS.success,
    fontSize: 11,
    fontWeight: '800',
  },
  priceTag: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
  },
  optionSub: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  addressBox: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  addressLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '700',
    marginBottom: 4,
  },
  addressInput: {
    color: COLORS.text,
    fontSize: 13,
    minHeight: 44,
  },
  totalCard: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginVertical: SPACING.lg,
  },
  totalRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  totalLabel: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '800',
  },
  totalValue: {
    color: COLORS.primaryGlow,
    fontSize: 24,
    fontWeight: '900',
  },
  payBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 16,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  payText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
