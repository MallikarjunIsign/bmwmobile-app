import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Image } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService } from '../services/api';
import { Purchase } from '../types/models';
import { Badge } from '../components/Badge';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'MyPurchases'>,
  NativeStackScreenProps<RootStackParamList>
>;

export const MyPurchasesScreen: React.FC<Props> = ({ navigation }) => {
  const [purchases, setPurchases] = useState<Purchase[]>([]);

  useEffect(() => {
    ApiService.getPurchases().then(setPurchases);
  }, []);

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>My Purchased Parts & Pickup Passes</Text>
      </View>

      <ScrollView contentContainerStyle={styles.scroll}>
        {purchases.map(item => (
          <TouchableOpacity
            key={item.id}
            style={styles.card}
            onPress={() => navigation.navigate('PickupReceive', { purchase: item })}
            activeOpacity={0.9}
          >
            <View style={styles.cardTop}>
              <Image 
                source={{ uri: item.imageUrl || 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop' }} 
                style={styles.thumb} 
              />
              <View style={styles.cardHeaderCol}>
                <Badge label={item.shippingStatus} variant="success" size="sm" />
                <Text style={styles.itemTitle} numberOfLines={2}>{item.itemTitle}</Text>
                <Text style={styles.totalPaid}>Paid Total: ${item.totalAmount.toLocaleString()}</Text>
              </View>
            </View>

            <View style={styles.passStrip}>
              <View>
                <Text style={styles.passLabel}>CUSTOMS PICKUP PASS CODE</Text>
                <Text style={styles.passCode}>{item.qrPassCode}</Text>
              </View>
              <View style={styles.portBox}>
                <Text style={styles.portLabel}>{item.pickupPort}</Text>
              </View>
            </View>

            <View style={styles.actionRow}>
              <TouchableOpacity 
                style={styles.qrBtn}
                onPress={() => navigation.navigate('PickupReceive', { purchase: item })}
              >
                <Text style={styles.qrBtnText}>Show QR Pass & Rate Seller →</Text>
              </TouchableOpacity>
            </View>
          </TouchableOpacity>
        ))}
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
    padding: SPACING.md,
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
  },
  scroll: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    padding: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  cardTop: {
    flexDirection: 'row',
    gap: SPACING.md,
  },
  thumb: {
    width: 80,
    height: 80,
    borderRadius: RADIUS.md,
    backgroundColor: COLORS.card,
  },
  cardHeaderCol: {
    flex: 1,
  },
  itemTitle: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '700',
    marginTop: 4,
  },
  totalPaid: {
    color: COLORS.primaryGlow,
    fontSize: 14,
    fontWeight: '800',
    marginTop: 2,
  },
  passStrip: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    marginVertical: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  passLabel: {
    color: COLORS.textMuted,
    fontSize: 10,
    fontWeight: '800',
  },
  passCode: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '900',
    letterSpacing: 1,
    marginTop: 2,
  },
  portBox: {
    backgroundColor: 'rgba(59, 130, 246, 0.15)',
    paddingHorizontal: SPACING.sm,
    paddingVertical: 4,
    borderRadius: RADIUS.sm,
  },
  portLabel: {
    color: COLORS.containerBlue,
    fontSize: 11,
    fontWeight: '700',
  },
  actionRow: {
    alignItems: 'flex-end',
  },
  qrBtn: {
    backgroundColor: COLORS.primary,
    paddingHorizontal: SPACING.md,
    paddingVertical: 10,
    borderRadius: RADIUS.md,
  },
  qrBtnText: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '800',
  },
});
