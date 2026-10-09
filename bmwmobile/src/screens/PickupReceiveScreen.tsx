import React, { useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView, TextInput, Modal } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { ApiService, MOCK_PURCHASES } from '../services/api';
import { useMoney } from '../location/useMoney';

type Props = NativeStackScreenProps<RootStackParamList, 'PickupReceive'>;

export const PickupReceiveScreen: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const { purchase: initialPurchase } = route.params;
  const purchase = initialPurchase || MOCK_PURCHASES[0];

  const [ratingModalVisible, setRatingModalVisible] = useState(false);
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState('Outstanding packaging and exact match for S58 engine. Fast customs clearance!');
  const [submitted, setSubmitted] = useState(false);

  const handleRatingSubmit = async () => {
    await ApiService.submitRating(purchase.id, rating, comment);
    setSubmitted(true);
    setRatingModalVisible(false);
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Pickup Pass & Receive" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* Pass Banner */}
        <View style={styles.qrCard}>
          <Text style={styles.qrHeader}>PORT WAREHOUSE PICKUP PASS</Text>
          <View style={styles.qrDisplay}>
            {/* Visual Barcode simulation */}
            <View style={styles.barcodeLines}>
              {[...Array(24)].map((_, i) => (
                <View key={i} style={[styles.barLine, { width: (i % 3 === 0 ? 4 : i % 2 === 0 ? 2 : 1) }]} />
              ))}
            </View>
            <Text style={styles.passCode}>{purchase.qrPassCode}</Text>
            <Text style={styles.passSub}>Present to OKOH Gate Officer at Port</Text>
          </View>
        </View>

        {/* Warehouse Location Info */}
        <View style={styles.card}>
          <Text style={styles.sectionTitle}>📍 PICKUP LOCATION ADDRESS</Text>
          <Text style={styles.locName}>OKOH Logistics Port Warehouse - Terminal B</Text>
          <Text style={styles.locAddr}>Gate 4, Apapa Commercial Complex, Apapa Port, Lagos, Nigeria</Text>
          <Text style={styles.locHours}>Hours: Mon - Sat (8:00 AM - 5:30 PM)</Text>
        </View>

        {/* Item & Container Summary */}
        <View style={styles.card}>
          <Text style={styles.sectionTitle}>📦 ITEM & CONTAINER SUMMARY</Text>
          <Text style={styles.itemTitle}>{purchase.itemTitle}</Text>

          <View style={styles.row}>
            <Text style={styles.key}>Container Number:</Text>
            <Text style={styles.val}>{purchase.containerNumber || 'MSCU7821940'}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Total Paid:</Text>
            <Text style={styles.valSuccess}>{money(purchase.totalAmount)}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Current Status:</Text>
            <Text style={styles.valSuccess}>{purchase.shippingStatus}</Text>
          </View>
        </View>

        {/* Rating Submission / Confirmation Button */}
        {submitted ? (
          <View style={styles.ratingDoneCard}>
            <Text style={styles.doneTitle}>✓ Item Marked as Received & Rated!</Text>
            <Text style={styles.doneStars}>Rating: {'★'.repeat(rating)} ({rating}/5 Stars)</Text>
            <Text style={styles.doneComment}>"{comment}"</Text>
          </View>
        ) : (
          <TouchableOpacity 
            style={styles.receiveBtn}
            onPress={() => setRatingModalVisible(true)}
            activeOpacity={0.8}
          >
            <Text style={styles.receiveText}>MARK AS RECEIVED & RATE SELLER</Text>
          </TouchableOpacity>
        )}
      </ScrollView>

      {/* Rating Modal */}
      <Modal visible={ratingModalVisible} transparent animationType="slide">
        <View style={styles.modalOverlay}>
          <View style={styles.modalContent}>
            <Text style={styles.modalTitle}>Rate Your Part & Seller</Text>
            <Text style={styles.modalSub}>Did the part arrive in described condition?</Text>

            {/* Stars Selector */}
            <View style={styles.starRow}>
              {[1, 2, 3, 4, 5].map(star => (
                <TouchableOpacity key={star} onPress={() => setRating(star)}>
                  <Text style={[styles.starIcon, rating >= star && styles.starActive]}>★</Text>
                </TouchableOpacity>
              ))}
            </View>

            <Text style={styles.commentLabel}>REVIEW & FEEDBACK</Text>
            <TextInput
              style={styles.commentInput}
              value={comment}
              onChangeText={setComment}
              multiline
            />

            <View style={styles.modalActions}>
              <TouchableOpacity 
                style={styles.modalCancel}
                onPress={() => setRatingModalVisible(false)}
              >
                <Text style={styles.modalCancelText}>Cancel</Text>
              </TouchableOpacity>

              <TouchableOpacity 
                style={styles.modalSubmit}
                onPress={handleRatingSubmit}
              >
                <Text style={styles.modalSubmitText}>Submit Rating</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
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
  qrCard: {
    backgroundColor: COLORS.surface,
    padding: SPACING.lg,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    alignItems: 'center',
    marginBottom: SPACING.md,
  },
  qrHeader: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.md,
  },
  qrDisplay: {
    backgroundColor: '#FFFFFF',
    padding: SPACING.lg,
    borderRadius: RADIUS.md,
    width: '100%',
    alignItems: 'center',
  },
  barcodeLines: {
    flexDirection: 'row',
    height: 50,
    alignItems: 'center',
    gap: 3,
    marginBottom: SPACING.md,
  },
  barLine: {
    height: '100%',
    backgroundColor: '#000000',
  },
  passCode: {
    color: '#000000',
    fontSize: 28,
    fontWeight: '900',
    letterSpacing: 4,
  },
  passSub: {
    color: '#64748B',
    fontSize: 11,
    marginTop: 4,
    fontWeight: '600',
  },
  card: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  sectionTitle: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  locName: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
  },
  locAddr: {
    color: COLORS.textSecondary,
    fontSize: 13,
    marginTop: 4,
    lineHeight: 18,
  },
  locHours: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 6,
  },
  itemTitle: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '700',
    marginBottom: SPACING.sm,
  },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: 4,
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
    fontSize: 12,
    fontWeight: '800',
  },
  receiveBtn: {
    backgroundColor: COLORS.success,
    paddingVertical: 16,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginTop: SPACING.sm,
  },
  receiveText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
    letterSpacing: 0.5,
  },
  ratingDoneCard: {
    backgroundColor: 'rgba(34, 164, 71, 0.15)',
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.success,
    alignItems: 'center',
  },
  doneTitle: {
    color: COLORS.success,
    fontWeight: '800',
    fontSize: 15,
  },
  doneStars: {
    color: COLORS.warningGlow,
    fontWeight: '800',
    fontSize: 14,
    marginVertical: 4,
  },
  doneComment: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontStyle: 'italic',
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.8)',
    justifyContent: 'center',
    padding: SPACING.lg,
  },
  modalContent: {
    backgroundColor: COLORS.surface,
    padding: SPACING.lg,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  modalTitle: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    textAlign: 'center',
  },
  modalSub: {
    color: COLORS.textMuted,
    fontSize: 13,
    textAlign: 'center',
    marginTop: 4,
    marginBottom: SPACING.md,
  },
  starRow: {
    flexDirection: 'row',
    justifyContent: 'center',
    gap: SPACING.sm,
    marginBottom: SPACING.md,
  },
  starIcon: {
    fontSize: 36,
    color: COLORS.borderHighlight,
  },
  starActive: {
    color: COLORS.warningGlow,
  },
  commentLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    marginBottom: 4,
  },
  commentInput: {
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    color: COLORS.text,
    padding: SPACING.md,
    height: 80,
    fontSize: 13,
    marginBottom: SPACING.lg,
  },
  modalActions: {
    flexDirection: 'row',
    gap: SPACING.sm,
  },
  modalCancel: {
    flex: 1,
    backgroundColor: COLORS.cardSecondary,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  modalCancelText: {
    color: COLORS.textMuted,
    fontWeight: '700',
  },
  modalSubmit: {
    flex: 1,
    backgroundColor: COLORS.primary,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  modalSubmitText: {
    color: COLORS.text,
    fontWeight: '800',
  },
});
