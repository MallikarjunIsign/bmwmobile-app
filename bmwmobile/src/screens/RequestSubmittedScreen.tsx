import React from 'react';
import { View, Text, StyleSheet, SafeAreaView, TouchableOpacity, ScrollView } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';

type Props = NativeStackScreenProps<RootStackParamList, 'RequestSubmitted'>;

export const RequestSubmittedScreen: React.FC<Props> = ({ route, navigation }) => {
  const { request } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>⚡</Text>
        </View>

        <Badge label="FEASIBILITY PASSED & RESERVED" variant="success" />

        <Text style={styles.title}>{request.partName}</Text>
        <Text style={styles.sub}>For {request.year} {request.make} {request.model}</Text>

        <View style={styles.card}>
          <Text style={styles.cardHeader}>CONTAINER FEASIBILITY REPORT</Text>
          <Text style={styles.noteText}>{request.feasibilityNote}</Text>

          <View style={styles.row}>
            <Text style={styles.key}>Status:</Text>
            <Text style={styles.valSuccess}>{request.status}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Assigned Container:</Text>
            <Text style={styles.val}>CN-2026-09 (Bremerhaven Hub)</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Port Destination:</Text>
            <Text style={styles.val}>{request.preferredPort}</Text>
          </View>
          <View style={styles.row}>
            <Text style={styles.key}>Max Budget Ceiling:</Text>
            <Text style={styles.val}>${request.maxBudget.toLocaleString()}</Text>
          </View>
        </View>

        <TouchableOpacity 
          style={styles.containerBtn}
          onPress={() => navigation.navigate('PlannedContainers')}
          activeOpacity={0.8}
        >
          <Text style={styles.containerText}>VIEW PLANNED CONTAINERS SCHEDULE →</Text>
        </TouchableOpacity>

        <TouchableOpacity 
          style={styles.secondaryBtn}
          onPress={() => navigation.navigate('MainTabs', { screen: 'Home' })}
        >
          <Text style={styles.secondaryText}>Return to Home Dashboard</Text>
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
