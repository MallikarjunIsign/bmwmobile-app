import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { PrimaryButton } from '../components/PrimaryButton';
import { Notice } from '../components/Notice';
import { ServerError } from '../services/api';
import { garageLabel, useGarage } from '../location/GarageContext';
import { useLocations } from '../location/LocationContext';
import { Field, MakeModelPicker, PickerField } from './sell/sellUi';

type Props = NativeStackScreenProps<RootStackParamList, 'Garage'>;

/** My Garage: save your vehicles; choosing one filters every search and the Home list by that vehicle. */
export const GarageScreen: React.FC<Props> = ({ navigation }) => {
  const { vehicles, selected, select, add, remove } = useGarage();
  const { country } = useLocations();
  const [make, setMake] = useState('');
  const [model, setModel] = useState('');
  const [year, setYear] = useState('');
  const [trim, setTrim] = useState('');
  const [engine, setEngine] = useState('');
  const [nickname, setNickname] = useState('');
  const [picker, setPicker] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const save = async () => {
    const y = parseInt(year, 10);
    if (!make || !model || !y) {
      setError('Choose the make and model and enter the year.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await add({ year: y, make, model, trim: trim || null, engine: engine || null, nickname: nickname || null });
      setMake('');
      setModel('');
      setYear('');
      setTrim('');
      setEngine('');
      setNickname('');
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't save the vehicle.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="My Garage" subtitle="Search shows parts that fit your vehicle" onBack={() => navigation.goBack()} />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        {vehicles.map(v => {
          const active = selected?.id === v.id;
          return (
            <View key={v.id} style={[styles.card, active && styles.cardActive]}>
              <TouchableOpacity style={styles.flex} onPress={() => select(active ? null : v.id!)}>
                <Text style={styles.name}>🚗 {garageLabel(v)}</Text>
                <Text style={styles.meta}>{active ? '✓ Filtering searches by this vehicle' : 'Tap to filter searches by this vehicle'}</Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={() => Alert.alert('Remove this vehicle?', garageLabel(v), [
                  { text: 'Keep', style: 'cancel' },
                  { text: 'Remove', style: 'destructive', onPress: () => remove(v.id!).catch(() => undefined) },
                ])}
              >
                <Text style={styles.remove}>Remove</Text>
              </TouchableOpacity>
            </View>
          );
        })}

        <Text style={styles.section}>ADD A VEHICLE</Text>
        <PickerField label="Make & model" value={make ? `${make} ${model}` : null} placeholder="Choose make and model" onPress={() => setPicker(true)} />
        <Field label="Year" keyboardType="number-pad" maxLength={4} value={year} onChangeText={setYear} />
        <Field label="Trim" optional value={trim} onChangeText={setTrim} />
        <Field label="Engine" optional value={engine} onChangeText={setEngine} />
        <Field label="Nickname" optional placeholder="Daily car" value={nickname} onChangeText={setNickname} />
        {error ? <Notice tone="error" message={error} /> : null}
        <PrimaryButton label="Save vehicle" onPress={save} loading={saving} style={styles.button} />
      </ScrollView>
      <MakeModelPicker
        visible={picker}
        country={country}
        onClose={() => setPicker(false)}
        onPick={(m, v) => {
          setMake(m.name);
          setModel(v.name);
          setPicker(false);
        }}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  flex: { flex: 1 },
  card: {
    flexDirection: 'row', alignItems: 'center', gap: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.lg,
    borderWidth: 1, borderColor: COLORS.border, padding: SPACING.md, marginBottom: SPACING.sm,
  },
  cardActive: { borderColor: COLORS.primaryGlow },
  name: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  meta: { color: COLORS.textMuted, fontSize: 12, marginTop: 2 },
  remove: { color: COLORS.danger, fontWeight: '700', fontSize: 13 },
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.lg },
  button: { marginTop: SPACING.lg },
});
