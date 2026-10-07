import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, SafeAreaView, Switch } from 'react-native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { BuyerType, PickupPort, VehicleInfo } from '../types/models';

type Props = NativeStackScreenProps<RootStackParamList, 'ProfilePreferences'>;

const BUYER_TYPES: BuyerType[] = ['Workshop / Garage', 'Wholesale', 'Individual'];
const NIGERIAN_PORTS: PickupPort[] = [
  'Lagos Apapa Port',
  'Lagos Tin Can Island',
  'Lagos Lekki Deep Sea Port',
  'Port Harcourt Onne Port',
  'Calabar Port'
];

export const ProfilePreferencesScreen: React.FC<Props> = ({ navigation }) => {
  const [buyerType, setBuyerType] = useState<BuyerType>('Workshop / Garage');
  const [preferredPort, setPreferredPort] = useState<PickupPort>('Lagos Apapa Port');
  const [garage, setGarage] = useState<VehicleInfo[]>([
    { id: 'v1', make: 'BMW', model: '330i xDrive', year: 2018, nickname: 'Daily Driver' },
    { id: 'v2', make: 'BMW', model: 'M4 Competition', year: 2021, nickname: 'Track Car' },
  ]);
  const [smsAlerts, setSmsAlerts] = useState(true);

  const handleSave = () => {
    navigation.reset({
      index: 0,
      routes: [{ name: 'MainTabs' }],
    });
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Profile & Preferences" subtitle="Customize pickup ports & BMW garage" />

      <ScrollView contentContainerStyle={styles.scrollContent}>
        {/* Section 1: Buyer Type */}
        <Text style={styles.sectionTitle}>1. BUYER PROFILE TYPE</Text>
        <View style={styles.chipRow}>
          {BUYER_TYPES.map(type => (
            <TouchableOpacity
              key={type}
              style={[styles.chip, buyerType === type && styles.chipActive]}
              onPress={() => setBuyerType(type)}
            >
              <Text style={[styles.chipText, buyerType === type && styles.chipTextActive]}>
                {type}
              </Text>
            </TouchableOpacity>
          ))}
        </View>

        {/* Section 2: Preferred Nigerian Pickup Port */}
        <Text style={styles.sectionTitle}>2. PREFERRED NIGERIAN PICKUP PORT</Text>
        <Text style={styles.sectionSub}>Containers with won parts will default to this clearing port.</Text>
        
        <View style={styles.cardList}>
          {NIGERIAN_PORTS.map(port => {
            const isSelected = preferredPort === port;
            return (
              <TouchableOpacity
                key={port}
                style={[styles.portCard, isSelected && styles.portCardSelected]}
                onPress={() => setPreferredPort(port)}
                activeOpacity={0.8}
              >
                <View style={styles.radio}>
                  {isSelected && <View style={styles.radioInner} />}
                </View>
                <Text style={[styles.portName, isSelected && styles.portNameSelected]}>
                  {port}
                </Text>
              </TouchableOpacity>
            );
          })}
        </View>

        {/* Section 3: Garage Vehicles */}
        <Text style={styles.sectionTitle}>3. YOUR BMW GARAGE</Text>
        <Text style={styles.sectionSub}>Parts search will automatically show compatibility tags.</Text>
        
        {garage.map(car => (
          <View key={car.id} style={styles.carCard}>
            <View>
              <Text style={styles.carName}>{car.year} {car.make} {car.model}</Text>
              <Text style={styles.carNick}>{car.nickname}</Text>
            </View>
            <TouchableOpacity onPress={() => setGarage(garage.filter(c => c.id !== car.id))}>
              <Text style={styles.removeText}>Remove</Text>
            </TouchableOpacity>
          </View>
        ))}

        <TouchableOpacity 
          style={styles.addCarBtn}
          onPress={() => {
            const newCar: VehicleInfo = {
              id: 'v-' + Date.now(),
              make: 'BMW',
              model: 'X5 xDrive40i',
              year: 2022,
              nickname: 'SUV'
            };
            setGarage([...garage, newCar]);
          }}
        >
          <Text style={styles.addCarText}>+ Add Another BMW Vehicle</Text>
        </TouchableOpacity>

        {/* Section 4: Notifications */}
        <Text style={styles.sectionTitle}>4. AUCTION & CONTAINER ALERTS</Text>
        <View style={styles.switchRow}>
          <Text style={styles.switchLabel}>Container Port Customs Cutoff SMS Alerts</Text>
          <Switch 
            value={smsAlerts} 
            onValueChange={setSmsAlerts}
            trackColor={{ false: COLORS.border, true: COLORS.primary }}
          />
        </View>

        <TouchableOpacity style={styles.saveBtn} onPress={handleSave} activeOpacity={0.8}>
          <Text style={styles.saveText}>SAVE PREFERENCES & EXPLORE</Text>
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
  scrollContent: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  sectionTitle: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '800',
    letterSpacing: 1,
    marginTop: SPACING.lg,
    marginBottom: SPACING.xs,
  },
  sectionSub: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginBottom: SPACING.md,
  },
  chipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: SPACING.sm,
    marginBottom: SPACING.sm,
  },
  chip: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.sm + 2,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  chipActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  chipText: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontWeight: '600',
  },
  chipTextActive: {
    color: COLORS.text,
    fontWeight: '700',
  },
  cardList: {
    gap: SPACING.xs + 2,
  },
  portCard: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  portCardSelected: {
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
  portName: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
  },
  portNameSelected: {
    color: COLORS.text,
    fontWeight: '700',
  },
  carCard: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginBottom: SPACING.sm,
  },
  carName: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '700',
  },
  carNick: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  removeText: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
  },
  addCarBtn: {
    backgroundColor: COLORS.cardSecondary,
    paddingVertical: SPACING.sm + 4,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    borderStyle: 'dashed',
    marginBottom: SPACING.md,
  },
  addCarText: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '700',
  },
  switchRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    marginVertical: SPACING.md,
  },
  switchLabel: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '600',
    flex: 1,
    marginRight: SPACING.sm,
  },
  saveBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginTop: SPACING.lg,
  },
  saveText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 1,
  },
});
