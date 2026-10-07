import React, { useState } from 'react';
import { View, Text, StyleSheet, TextInput, ScrollView, TouchableOpacity, SafeAreaView } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { ApiService } from '../services/api';

type Props = NativeStackScreenProps<RootStackParamList, 'OrderPartRequest'>;

export const OrderPartRequestScreen: React.FC<Props> = ({ route, navigation }) => {
  const { prefillMake, prefillModel, prefillPart } = route.params || {};

  const [make, setMake] = useState(prefillMake || 'BMW');
  const [model, setModel] = useState(prefillModel || 'M3 CS / 330i G20');
  const [year, setYear] = useState('2021');
  const [partName, setPartName] = useState(prefillPart || 'S58 Engine Wiring Harness & ECU');
  const [vin, setVin] = useState('WBS83AY050FP92014');
  const [specs, setSpecs] = useState('Grade A original OEM part from Germany. No cracks.');
  const [maxBudget, setMaxBudget] = useState('2500');
  const [preferredPort, setPreferredPort] = useState('Lagos Apapa Port');
  const [checkingFeasibility, setCheckingFeasibility] = useState(false);

  const handleSubmit = async () => {
    setCheckingFeasibility(true);
    const request = await ApiService.createPartRequest({
      make,
      model,
      year: parseInt(year, 10) || 2021,
      partName,
      vin,
      specs,
      maxBudget: parseFloat(maxBudget) || 2500,
      preferredPort,
    });
    setCheckingFeasibility(false);
    navigation.replace('RequestSubmitted', { request });
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Order a Custom BMW Part" subtitle="Instant German sourcing & container feasibility" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* Form Fields */}
        <Text style={styles.label}>BMW VEHICLE MAKE</Text>
        <TextInput style={styles.input} value={make} onChangeText={setMake} />

        <Text style={styles.label}>BMW MODEL & TRIM</Text>
        <TextInput style={styles.input} value={model} onChangeText={setModel} />

        <Text style={styles.label}>MODEL YEAR</Text>
        <TextInput style={styles.input} value={year} onChangeText={setYear} keyboardType="numeric" />

        <Text style={styles.label}>PART NAME & DESCRIPTION</Text>
        <TextInput style={styles.input} value={partName} onChangeText={setPartName} />

        <Text style={styles.label}>VEHICLE VIN NUMBER (OPTIONAL)</Text>
        <TextInput style={styles.input} value={vin} onChangeText={setVin} placeholder="17-Digit VIN" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>ADDITIONAL SPECIFICATIONS / NOTES</Text>
        <TextInput style={[styles.input, { height: 70 }]} value={specs} onChangeText={setSpecs} multiline />

        <Text style={styles.label}>MAXIMUM BUDGET CEILING ($ USD)</Text>
        <TextInput style={styles.input} value={maxBudget} onChangeText={setMaxBudget} keyboardType="numeric" />

        <Text style={styles.label}>TARGET NIGERIAN CLEARING PORT</Text>
        <View style={styles.portRow}>
          {['Lagos Apapa Port', 'Lagos Tin Can', 'Onne Port'].map(p => (
            <TouchableOpacity 
              key={p} 
              style={[styles.portChip, preferredPort.includes(p) && styles.portChipActive]}
              onPress={() => setPreferredPort(p)}
            >
              <Text style={[styles.portText, preferredPort.includes(p) && styles.portTextActive]}>{p}</Text>
            </TouchableOpacity>
          ))}
        </View>

        {/* Feasibility Schedule Info Box */}
        <View style={styles.feasibilityBox}>
          <Text style={styles.feaTitle}>⚡ INSTANT CONTAINER FEASIBILITY CHECK</Text>
          <Text style={styles.feaText}>
            Our system will automatically check space cutoff for Container <Text style={styles.bold}>CN-2026-09</Text> leaving Bremerhaven.
          </Text>
        </View>

        <TouchableOpacity 
          style={styles.submitBtn} 
          onPress={handleSubmit} 
          disabled={checkingFeasibility}
          activeOpacity={0.8}
        >
          <Text style={styles.submitText}>
            {checkingFeasibility ? 'CHECKING FEASIBILITY & SUBMITTING...' : 'CHECK FEASIBILITY & SUBMIT REQUEST'}
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
  label: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginTop: SPACING.md,
    marginBottom: SPACING.xs,
  },
  input: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.md,
    color: COLORS.text,
    fontSize: 15,
    paddingHorizontal: SPACING.md,
    height: 48,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  portRow: {
    flexDirection: 'row',
    gap: SPACING.xs,
    marginVertical: SPACING.xs,
  },
  portChip: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 2,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  portChipActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  portText: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '600',
  },
  portTextActive: {
    color: COLORS.text,
    fontWeight: '700',
  },
  feasibilityBox: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginVertical: SPACING.lg,
  },
  feaTitle: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: 4,
  },
  feaText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    lineHeight: 16,
  },
  bold: {
    color: COLORS.text,
    fontWeight: '700',
  },
  submitBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 16,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  submitText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
