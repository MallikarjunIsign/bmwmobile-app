import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';
import { useMoney } from '../location/useMoney';
import { useLocations } from '../location/LocationContext';
import { FEATURES } from '../config';
import { formatRadius } from '../utils/geo';

type Props = NativeStackScreenProps<RootStackParamList, 'RequestSubmitted'>;

/** "Custom order created" – the order is Open until a matching listing is published. */
export const RequestSubmittedScreen: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const { country } = useLocations();
  const { request } = route.params;
  const vehicle = [request.year || null, request.make, request.model].filter(Boolean).join(' ');

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>🔔</Text>
        </View>

        <Badge label="CUSTOM ORDER CREATED" variant="success" />

        <Text style={styles.title}>{request.partName || request.partNumber || request.searchText}</Text>
        <Text style={styles.sub}>{vehicle ? `For ${vehicle}` : 'For any vehicle'}</Text>

        <View style={styles.card}>
          <Text style={styles.cardHeader}>WE'LL NOTIFY YOU</Text>
          <Text style={styles.noteText}>
            When a matching part is listed
            {request.radiusKm ? ` within ${formatRadius(request.radiusKm, country)}` : ''}
            {request.locationName ? ` of ${request.locationName}` : ''}, you'll get a notification that opens the listing.
          </Text>

          <View style={styles.row}>
            <Text style={styles.key}>Status:</Text>
            <Text style={styles.valSuccess}>{request.status}</Text>
          </View>
          {request.partNumber ? (
            <View style={styles.row}>
              <Text style={styles.key}>Part number:</Text>
              <Text style={styles.val}>{request.partNumber}</Text>
            </View>
          ) : null}
          {request.maxBudget > 0 ? (
            <View style={styles.row}>
              <Text style={styles.key}>Max budget:</Text>
              <Text style={styles.val}>{money(request.maxBudget)}</Text>
            </View>
          ) : null}
          {/* Container logistics (hidden while FEATURES.containers is off) */}
          {FEATURES.containers ? (
            <View style={styles.row}>
              <Text style={styles.key}>Port Destination:</Text>
              <Text style={styles.val}>{request.preferredPort}</Text>
            </View>
          ) : null}
        </View>

        <TouchableOpacity 
          style={styles.containerBtn}
          onPress={() => navigation.navigate('MainTabs', { screen: 'MyPurchases' }, { pop: true })}
          activeOpacity={0.8}
        >
          <Text style={styles.containerText}>VIEW MY CUSTOM ORDERS →</Text>
        </TouchableOpacity>

        <TouchableOpacity 
          style={styles.secondaryBtn}
          onPress={() => navigation.navigate('MainTabs', { screen: 'Home' }, { pop: true })}
        >
          <Text style={styles.secondaryText}>Return to Home</Text>
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
    backgroundColor: 'rgba(161, 0, 255, 0.2)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 2,
    borderColor: COLORS.primaryGlow,
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
    marginTop: SPACING.sm,
  },
  sub: {
    color: COLORS.textMuted,
    fontSize: 13,
    marginBottom: SPACING.lg,
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
  cardHeader: {
    color: COLORS.success,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: 4,
  },
  noteText: {
    color: COLORS.textSecondary,
    fontSize: 13,
    lineHeight: 18,
    marginBottom: SPACING.md,
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
    fontSize: 12,
    fontWeight: '800',
  },
  containerBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginBottom: SPACING.sm,
  },
  containerText: {
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
