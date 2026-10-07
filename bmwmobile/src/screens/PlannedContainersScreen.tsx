import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, SafeAreaView } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { ApiService } from '../services/api';
import { ContainerSchedule } from '../types/models';
import { Badge } from '../components/Badge';

type Props = NativeStackScreenProps<RootStackParamList, 'PlannedContainers'>;

export const PlannedContainersScreen: React.FC<Props> = ({ navigation }) => {
  const [schedules, setSchedules] = useState<ContainerSchedule[]>([]);

  useEffect(() => {
    ApiService.getPlannedContainers().then(setSchedules);
  }, []);

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Planned Containers Feasibility" subtitle="Upcoming German shipments & customs cutoffs" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {schedules.map(cnt => (
          <View key={cnt.id} style={styles.card}>
            <View style={styles.cardTop}>
              <View>
                <Text style={styles.code}>{cnt.code}</Text>
                <Text style={styles.route}>{cnt.originPort} ➔ {cnt.destinationPort}</Text>
              </View>
              <Badge label={`${cnt.daysRemaining} DAYS LEFT`} variant={cnt.daysRemaining < 15 ? 'warning' : 'info'} />
            </View>

            {/* Capacity Progress Bar */}
            <View style={styles.progressWrap}>
              <View style={styles.progressHeader}>
                <Text style={styles.progressLabel}>Container Capacity Filled</Text>
                <Text style={styles.progressVal}>{cnt.capacityPercentage}% Full</Text>
              </View>
              <View style={styles.track}>
                <View style={[styles.bar, { width: `${cnt.capacityPercentage}%` }]} />
              </View>
            </View>

            {/* Dates Grid */}
            <View style={styles.datesGrid}>
              <View style={styles.dateCol}>
                <Text style={styles.dateLabel}>Cutoff Date</Text>
                <Text style={styles.dateVal}>Oct 15, 2026</Text>
              </View>
              <View style={styles.dateCol}>
                <Text style={styles.dateLabel}>Dep. (ETD)</Text>
                <Text style={styles.dateVal}>Oct 21, 2026</Text>
              </View>
              <View style={styles.dateCol}>
                <Text style={styles.dateLabel}>Est. Arrival</Text>
                <Text style={styles.dateValHighlight}>Nov 02, 2026</Text>
              </View>
            </View>

            <TouchableOpacity 
              style={styles.packBtn}
              onPress={() => navigation.navigate('OrderPartRequest', {})}
              activeOpacity={0.8}
            >
              <Text style={styles.packText}>PACK CUSTOM PART IN {cnt.code} →</Text>
            </TouchableOpacity>
          </View>
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
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    marginBottom: SPACING.md,
  },
  code: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '900',
  },
  route: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  progressWrap: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    marginBottom: SPACING.md,
  },
  progressHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 6,
  },
  progressLabel: {
    color: COLORS.textMuted,
    fontSize: 11,
  },
  progressVal: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
  },
  track: {
    height: 8,
    backgroundColor: COLORS.surface,
    borderRadius: 4,
    overflow: 'hidden',
  },
  bar: {
    height: '100%',
    backgroundColor: COLORS.primaryGlow,
    borderRadius: 4,
  },
  datesGrid: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: SPACING.md,
    paddingHorizontal: SPACING.xs,
  },
  dateCol: {
    alignItems: 'center',
  },
  dateLabel: {
    color: COLORS.textMuted,
    fontSize: 10,
    textTransform: 'uppercase',
  },
  dateVal: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '700',
    marginTop: 2,
  },
  dateValHighlight: {
    color: COLORS.success,
    fontSize: 12,
    fontWeight: '800',
    marginTop: 2,
  },
  packBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 12,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  packText: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
});
