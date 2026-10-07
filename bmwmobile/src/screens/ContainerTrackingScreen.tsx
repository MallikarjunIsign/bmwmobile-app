import React, { useState } from 'react';
import { View, Text, StyleSheet, TextInput, ScrollView, TouchableOpacity, SafeAreaView } from 'react-native';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { StatusTimeline } from '../components/StatusTimeline';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Tracking'>,
  NativeStackScreenProps<RootStackParamList>
>;

export const ContainerTrackingScreen: React.FC<Props> = ({ route }) => {
  const [containerNum, setContainerNum] = useState('MSCU7821940');

  const trackingSteps = [
    { title: 'Order & Customs Papers Confirmed', subtitle: 'Bremerhaven Port Warehouse', date: 'Oct 01, 2026', completed: true, active: false },
    { title: 'Container Loaded & Sealed', subtitle: 'Container MSCU7821940 (40ft High Cube)', date: 'Oct 03, 2026', completed: true, active: false },
    { title: 'Vessel Departed Origin Port', subtitle: 'Cargo Vessel: CMA CGM MUNCHEN', date: 'Oct 06, 2026', completed: true, active: false },
    { title: 'Arrived at Lagos Apapa Customs Port', subtitle: 'Vessel berth & container discharge', date: 'Oct 28, 2026', completed: true, active: false },
    { title: 'Customs Clearing & Inspection', subtitle: 'OKOH Logistics Clearing Agent', date: 'Oct 30, 2026', completed: false, active: true },
    { title: 'Ready for Warehouse Pickup / Delivery', subtitle: 'QR Pass Code PU-1042-7731 Ready', date: 'Nov 02, 2026', completed: false, active: false },
  ];

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Container & Shipment Tracking</Text>
      </View>

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* Search Input */}
        <View style={styles.searchBox}>
          <Text style={styles.inputLabel}>ENTER CONTAINER NUMBER OR VIN</Text>
          <View style={styles.inputRow}>
            <TextInput
              style={styles.input}
              value={containerNum}
              onChangeText={setContainerNum}
              placeholder="e.g. MSCU7821940 or CN-2026-09"
              placeholderTextColor={COLORS.textMuted}
            />
            <TouchableOpacity style={styles.trackBtn}>
              <Text style={styles.trackBtnText}>Track</Text>
            </TouchableOpacity>
          </View>
        </View>

        {/* Container Overview Card */}
        <View style={styles.card}>
          <View style={styles.cardHeader}>
            <View>
              <Text style={styles.containerCode}>{containerNum}</Text>
              <Text style={styles.routeText}>Bremerhaven, DE ➔ Lagos Apapa, NG</Text>
            </View>
            <View style={styles.statusBadge}>
              <Text style={styles.statusBadgeText}>IN CUSTOMS CLEARING</Text>
            </View>
          </View>

          <View style={styles.metaGrid}>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>Vessel Name</Text>
              <Text style={styles.gridVal}>CMA CGM MUNCHEN</Text>
            </View>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>Est. Arrival</Text>
              <Text style={styles.gridValHighlight}>Nov 02, 2026</Text>
            </View>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>Clearing Agent</Text>
              <Text style={styles.gridVal}>OKOH Logistics NG</Text>
            </View>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>Items Packed</Text>
              <Text style={styles.gridVal}>14 Parts / Engines</Text>
            </View>
          </View>
        </View>

        {/* Shipment Timeline Progress */}
        <View style={styles.card}>
          <Text style={styles.sectionTitle}>🚢 LIVE SHIPMENT TIMELINE</Text>
          <StatusTimeline steps={trackingSteps} />
        </View>
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
  searchBox: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  inputLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  inputRow: {
    flexDirection: 'row',
    gap: SPACING.xs,
  },
  input: {
    flex: 1,
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    color: COLORS.text,
    fontSize: 15,
    paddingHorizontal: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
  },
  trackBtn: {
    backgroundColor: COLORS.primary,
    paddingHorizontal: SPACING.lg,
    justifyContent: 'center',
    borderRadius: RADIUS.md,
  },
  trackBtnText: {
    color: COLORS.text,
    fontWeight: '800',
    fontSize: 14,
  },
  card: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.md,
  },
  cardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
    paddingBottom: SPACING.sm,
    marginBottom: SPACING.md,
  },
  containerCode: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '900',
  },
  routeText: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  statusBadge: {
    backgroundColor: 'rgba(245, 176, 20, 0.15)',
    paddingHorizontal: SPACING.sm,
    paddingVertical: 4,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.warning,
  },
  statusBadgeText: {
    color: COLORS.warningGlow,
    fontSize: 10,
    fontWeight: '800',
  },
  metaGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: SPACING.sm,
  },
  gridItem: {
    width: '47%',
    backgroundColor: COLORS.card,
    padding: SPACING.sm,
    borderRadius: RADIUS.sm,
  },
  gridLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
  },
  gridVal: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '700',
    marginTop: 2,
  },
  gridValHighlight: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
    marginTop: 2,
  },
  sectionTitle: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.sm,
  },
});
