import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, ActivityIndicator, Alert, KeyboardAvoidingView, Platform, Switch } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../../types/navigation';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { Header } from '../../components/Header';
import { Notice } from '../../components/Notice';
import { PrimaryButton } from '../../components/PrimaryButton';
import { MarketApi } from '../../services/marketApi';
import { ServerError } from '../../services/api';
import { useLocations } from '../../location/LocationContext';
import { useMoney } from '../../location/useMoney';
import { REGIONS, distanceUnit } from '../../utils/geo';
import { label } from '../../utils/time';
import type { DeliveryOption, FieldErrors, Fitment, Listing, PartType, SaveListing, VehicleDetails } from '../../types/listing';
import { CheckboxRow } from '../../components/CheckboxRow';
import { shippingFeeText } from '../../utils/delivery';
import { ChipGroup, Field, FieldError, MakeModelPicker, PartTypePicker, PickerField, SectionTitle, styles as ui } from './sellUi';
import { PhotosStep, PHOTO_LIMITS } from './PhotosStep';
import { EndTimePicker, endProblem, formatEnd } from './EndTimePicker';
import { Countdown } from '../../components/Countdown';

type Props = NativeStackScreenProps<RootStackParamList, 'SellWizard'>;

type StepKey = 'partType' | 'specs' | 'fitment' | 'details' | 'identity' | 'condition' | 'papers' | 'photos' | 'location' | 'auction' | 'review';

const STEP_TITLES: Record<StepKey, string> = {
  partType: 'Part type',
  specs: 'Size / spec',
  fitment: 'Fits which vehicles',
  details: 'Part details',
  identity: 'Vehicle',
  condition: 'Condition',
  papers: 'Papers',
  photos: 'Photos',
  location: 'Location & delivery',
  auction: 'Price & how to sell',
  review: 'Review & publish',
};

/** Which step a server validation error belongs to. */
const FIELD_STEP: Record<string, StepKey> = {
  partTypeId: 'partType', kind: 'partType', fitments: 'fitment', specs: 'specs',
  brandType: 'details', brand: 'details', condition: 'details', usedGrade: 'details', position: 'details', side: 'details',
  quantity: 'details', description: 'details', photos: 'photos', location: 'location', countryCode: 'location',
  deliveryOptions: 'location', sizeClass: 'location', shippingFee: 'location', pickupAddress: 'location', partPrice: 'auction', durationDays: 'auction', auctionEnd: 'auction',
  saleType: 'auction',
  year: 'identity', make: 'identity', model: 'identity', fuel: 'identity', transmission: 'identity', vin: 'identity',
  runningStatus: 'condition', mileage: 'condition', damageType: 'condition', papers: 'papers',
};

const FUELS = ['Petrol', 'Diesel', 'CNG', 'LPG', 'Hybrid', 'Electric'];
const TRANSMISSIONS = ['Manual', 'Automatic', 'CVT', 'DCT', 'AMT'];
const DRIVETRAINS = ['FWD', 'RWD', '4WD', 'AWD'];
const BODY_TYPES = ['Hatchback', 'Sedan', 'SUV', 'Pickup', 'Van', 'Coupe', 'Wagon', 'Motorcycle', 'Truck'];
const YES_NO = ['Yes', 'No'];

/** Papers asked per country (same keys as the server). */
const PAPERS: Record<string, { key: string; label: string; options?: string[]; placeholder?: string }[]> = {
  IN: [
    { key: 'RcAvailable', label: 'RC (registration certificate) available', options: YES_NO },
    { key: 'Noc', label: 'NOC', options: ['Yes', 'No', 'Not needed'] },
    { key: 'InsuranceValidUntil', label: 'Insurance valid until', placeholder: 'YYYY-MM-DD' },
  ],
  US: [{ key: 'TitleStatus', label: 'Title status', options: ['Clean', 'Salvage', 'Rebuilt'] }],
  NG: [
    { key: 'CustomsPapers', label: 'Customs papers', options: YES_NO },
    { key: 'Registration', label: 'Registration', options: YES_NO },
    { key: 'UsedType', label: 'Used type', options: ['Foreign-used', 'Nigerian-used'] },
  ],
  IE: [
    { key: 'RegistrationCertificate', label: 'Registration certificate', options: YES_NO },
    { key: 'NctValidUntil', label: 'NCT valid until', placeholder: 'YYYY-MM-DD' },
  ],
};

const EMPTY_VEHICLE: VehicleDetails = { damageAreas: [], inspectionAllowed: false, papers: {} };

/** The server listing → the editable form. */
function formFrom(l: Listing): SaveListing {
  return {
    partTypeId: l.partTypeId ?? undefined,
    titleExtra: l.titleExtra ?? '',
    description: l.description ?? '',
    year: l.kind === 'Vehicle' ? l.year || undefined : undefined,
    make: l.kind === 'Vehicle' ? l.make : undefined,
    model: l.kind === 'Vehicle' ? l.model : undefined,
    colour: l.color ?? undefined,
    mileage: l.mileage ?? undefined,
    partDetails: l.partDetails ?? undefined,
    fitments: l.fitments ?? [],
    partNumbers: l.partNumberItems ?? [],
    specs: l.specs ?? {},
    vehicle: l.vehicle ?? (l.kind === 'Vehicle' ? EMPTY_VEHICLE : undefined),
    locationName: l.locationName ?? undefined,
    latitude: l.latitude ?? undefined,
    longitude: l.longitude ?? undefined,
    countryCode: l.countryCode ?? undefined,
    deliveryOptions: l.deliveryOptions ?? ['Pickup'],
    sizeClass: l.sizeClass ?? null,
    shippingFee: l.shippingFee ?? undefined,
    pickupAddress: l.pickupAddress ?? undefined,
    partPrice: l.startingPrice > 1 ? l.startingPrice : undefined,
    saleType: l.saleType ?? 'Auction',
    // Older drafts count days from publishing; newer ones have the end the seller chose.
    durationDays: l.durationDays ?? undefined,
    auctionEnd: l.durationDays ? undefined : l.auctionEnd,
  };
}

/** The end the seller sees: the chosen time, or (older drafts) N days from now. */
export function plannedEnd(form: Pick<SaveListing, 'auctionEnd' | 'durationDays'>, now = Date.now()): Date {
  if (form.auctionEnd) return new Date(form.auctionEnd);
  return new Date(now + (form.durationDays ?? 7) * 86400000);
}

/**
 * "+ Sell" for common parts, vehicle-specific parts and vehicles (design doc §4–§6). Every change is saved to the
 * draft automatically, so the seller can leave and continue later; publishing checks everything on the server.
 */
export const SellWizardScreen: React.FC<Props> = ({ route, navigation }) => {
  const { listingId } = route.params;
  const money = useMoney();
  const { location, country: myCountry } = useLocations();
  const [listing, setListing] = useState<Listing | null>(null);
  const [form, setForm] = useState<SaveListing>({});
  const [partType, setPartType] = useState<PartType | null>(null);
  const [stepIndex, setStepIndex] = useState(0);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [typePicker, setTypePicker] = useState(false);
  const [vehiclePicker, setVehiclePicker] = useState<number | 'identity' | null>(null);
  const [endPicker, setEndPicker] = useState(false);
  const [bidSteps, setBidSteps] = useState<{ from: number; to: number | null; step: number }[]>([]);
  const formRef = useRef(form);
  formRef.current = form;
  const dirty = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    MarketApi.getListing(listingId)
      .then(l => {
        setListing(l);
        setForm(formFrom(l));
        if (route.params.step) setStepIndex(0);
        if (l.partTypeId) MarketApi.partType(l.partTypeId).then(setPartType).catch(() => undefined);
      })
      .catch(e => setLoadError(e instanceof ServerError ? e.message : "Couldn't load the listing"));
    MarketApi.bidSteps().then(setBidSteps).catch(() => undefined);
  }, [listingId]); // eslint-disable-line react-hooks/exhaustive-deps

  const kind = listing?.kind ?? 'CommonPart';
  const country = (form.countryCode ?? myCountry) as keyof typeof REGIONS;

  const steps = useMemo<StepKey[]>(() => {
    if (kind === 'Vehicle') return ['identity', 'condition', 'papers', 'photos', 'location', 'auction', 'review'];
    if (kind === 'VehicleSpecificPart') return ['partType', 'fitment', 'details', 'photos', 'location', 'auction', 'review'];
    return partType?.fitmentMode === 'SizeBased'
      ? ['partType', 'specs', 'details', 'photos', 'location', 'auction', 'review']
      : ['partType', 'details', 'photos', 'location', 'auction', 'review'];
  }, [kind, partType?.fitmentMode]);

  // Jump to a step given in the route ("Edit photos").
  useEffect(() => {
    if (route.params.step && listing) {
      const i = steps.indexOf(route.params.step as StepKey);
      if (i >= 0) setStepIndex(i);
    }
  }, [listing?.id, steps]); // eslint-disable-line react-hooks/exhaustive-deps

  const step = steps[Math.min(stepIndex, steps.length - 1)];

  /** Saves the draft (only when something changed). */
  const save = useCallback(async (): Promise<Listing | null> => {
    if (timer.current) clearTimeout(timer.current);
    if (!dirty.current) return listing;
    dirty.current = false;
    setStatus('saving');
    try {
      const updated = await MarketApi.saveListing(listingId, formRef.current);
      setListing(updated);
      setStatus('saved');
      return updated;
    } catch (e) {
      dirty.current = true;
      setStatus('error');
      if (e instanceof ServerError) setErrors(e.fields);
      return null;
    }
  }, [listing, listingId]);

  /** Changes the form and autosaves shortly after the seller stops typing. */
  const update = useCallback(
    (patch: Partial<SaveListing>) => {
      setForm(f => ({ ...f, ...patch }));
      dirty.current = true;
      if (timer.current) clearTimeout(timer.current);
      timer.current = setTimeout(() => save(), 1200);
    },
    [save],
  );

  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  const details = form.partDetails ?? {};
  const setDetails = (patch: Partial<NonNullable<SaveListing['partDetails']>>) => update({ partDetails: { ...details, ...patch } });
  const vehicle = form.vehicle ?? EMPTY_VEHICLE;
  const setVehicle = (patch: Partial<VehicleDetails>) => update({ vehicle: { ...vehicle, ...patch } });
  const fitments = form.fitments ?? [];
  const setFitment = (i: number, patch: Partial<Fitment>) =>
    update({ fitments: fitments.map((f, n) => (n === i ? { ...f, ...patch } : f)) });

  const goTo = async (index: number) => {
    await save();
    setStepIndex(Math.max(0, Math.min(index, steps.length - 1)));
  };

  const publish = async () => {
    const saved = await save();
    if (!saved && dirty.current) return;
    setPublishing(true);
    setErrors({});
    try {
      const live = await MarketApi.publishListing(listingId);
      Alert.alert('Your listing is live', live.title ?? undefined);
      navigation.replace('PartDetails', { auctionId: live.id });
    } catch (e) {
      if (e instanceof ServerError) {
        setErrors(e.fields);
        if (Object.keys(e.fields).length === 0) Alert.alert('Not published', e.message);
      } else {
        Alert.alert('Not published', "Can't reach the server. Your draft is saved.");
      }
    } finally {
      setPublishing(false);
    }
  };

  if (loadError) {
    return (
      <SafeAreaView style={styles.container}>
        <Header title="Sell" onBack={() => navigation.goBack()} />
        <View style={styles.pad}><Notice tone="error" message={loadError} /></View>
      </SafeAreaView>
    );
  }
  if (!listing) {
    return (
      <SafeAreaView style={styles.container}>
        <ActivityIndicator color={COLORS.primaryGlow} style={styles.loader} />
      </SafeAreaView>
    );
  }

  const errorsFor = (...fields: string[]) => fields.flatMap(f => errors[f] ?? []);
  const stepHasErrors = (s: StepKey) => Object.keys(errors).some(f => FIELD_STEP[f] === s);
  const unit = distanceUnit(country);
  const currency = REGIONS[country]?.currencySymbol ?? '';

  // ── steps ──

  const renderPartType = () => (
    <View>
      <SectionTitle
        title="What part is it?"
        hint={kind === 'CommonPart'
          ? 'Common parts fit any vehicle: universal (dash cam, tool kit) or by size (tyre, bulb, battery, wiper blade, oil).'
          : 'Search or browse Category › Sub-category › Part type.'}
      />
      <PickerField label="Part type" value={partType?.name} placeholder="Search: bumper, bonnet, headlight…" onPress={() => setTypePicker(true)} />
      {partType ? (
        <Text style={ui.hint}>
          {[partType.category, partType.subCategory].filter(Boolean).join(' › ')} · {label(partType.fitmentMode)}
        </Text>
      ) : null}
      <FieldError messages={errorsFor('partTypeId', 'kind')} />
      <PartTypePicker
        visible={typePicker}
        commonOnly={kind === 'CommonPart'}
        onClose={() => setTypePicker(false)}
        onPick={t => {
          setTypePicker(false);
          setPartType(t);
          update({ partTypeId: t.id, specs: t.fitmentMode === 'SizeBased' ? form.specs : {} });
        }}
      />
    </View>
  );

  const renderSpecs = () => (
    <View>
      <SectionTitle title={`${partType?.name ?? 'Part'} size / spec`} hint="Only the size fields – no vehicle details for parts that fit by size." />
      {(partType?.specFields ?? []).map(f =>
        f.type === 'select' ? (
          <ChipGroup
            key={f.key}
            label={f.label}
            optional={!f.required}
            options={f.options}
            value={form.specs?.[f.key] ?? null}
            onChange={v => update({ specs: { ...form.specs, [f.key]: v ?? '' } })}
            clearable
          />
        ) : (
          <Field
            key={f.key}
            label={f.label}
            optional={!f.required}
            suffix={f.unit ?? undefined}
            keyboardType={f.type === 'number' ? 'numeric' : 'default'}
            value={form.specs?.[f.key] ?? ''}
            onChangeText={v => update({ specs: { ...form.specs, [f.key]: v } })}
          />
        ),
      )}
      <FieldError messages={errorsFor('specs')} />
    </View>
  );

  const renderFitment = () => {
    const important = new Set(partType?.importantFitmentFields ?? []);
    return (
      <View>
        <SectionTitle
          title="Which vehicles does it fit?"
          hint="Year from–to, make and model are required. Leave the rest as “Any” unless it matters for this part."
        />
        {fitments.map((f, i) => (
          <View key={i} style={ui.card}>
            <View style={styles.rowBetween}>
              <Text style={styles.cardTitle}>Vehicle {i + 1}</Text>
              <TouchableOpacity onPress={() => update({ fitments: fitments.filter((_, n) => n !== i) })}>
                <Text style={styles.remove}>Remove</Text>
              </TouchableOpacity>
            </View>
            <PickerField
              label="Make & model"
              value={f.make ? `${f.make} ${f.model}` : null}
              placeholder="Choose make and model"
              onPress={() => setVehiclePicker(i)}
            />
            <View style={styles.twoCols}>
              <View style={styles.col}>
                <Field label="Year from" keyboardType="number-pad" maxLength={4} value={f.yearFrom ? String(f.yearFrom) : ''}
                  onChangeText={v => setFitment(i, { yearFrom: parseInt(v, 10) || 0 })} />
              </View>
              <View style={styles.col}>
                <Field label="Year to" keyboardType="number-pad" maxLength={4} value={f.yearTo ? String(f.yearTo) : ''}
                  onChangeText={v => setFitment(i, { yearTo: parseInt(v, 10) || 0 })} />
              </View>
            </View>
            <Field label="Trim / variant" optional placeholder="Any" value={f.trim ?? ''} onChangeText={v => setFitment(i, { trim: v })} />
            <Field label="Engine" optional placeholder={important.has('Engine') ? 'Important for this part, e.g. 2.8 diesel' : 'Any'}
              value={f.engine ?? ''} onChangeText={v => setFitment(i, { engine: v })} />
            <ChipGroup label="Fuel" optional options={FUELS} value={f.fuel ?? null} onChange={v => setFitment(i, { fuel: v })} clearable />
            <ChipGroup label={important.has('Transmission') ? 'Transmission (important)' : 'Transmission'} optional options={TRANSMISSIONS}
              value={f.transmission ?? null} onChange={v => setFitment(i, { transmission: v })} clearable />
            <ChipGroup label={important.has('Drivetrain') ? 'Drivetrain (important)' : 'Drivetrain'} optional options={DRIVETRAINS}
              value={f.drivetrain ?? null} onChange={v => setFitment(i, { drivetrain: v })} clearable />
            <ChipGroup label={important.has('SteeringSide') ? 'Steering side (important)' : 'Steering side'} optional
              options={[{ value: 'LHD', label: 'LHD' }, { value: 'RHD', label: 'RHD' }]}
              value={f.steeringSide ?? null} onChange={v => setFitment(i, { steeringSide: v })} clearable />
            <Field label="Market" optional placeholder="e.g. Indian-market, US-market" value={f.market ?? ''} onChangeText={v => setFitment(i, { market: v })} />
            <Field label="Note" optional placeholder="e.g. only for models with fog lamps" value={f.note ?? ''} onChangeText={v => setFitment(i, { note: v })} />
          </View>
        ))}
        <View style={styles.linksRow}>
          {fitments.length < 50 ? (
            <TouchableOpacity onPress={() => update({ fitments: [...fitments, { yearFrom: 0, yearTo: 0, make: '', model: '' }] })}>
              <Text style={ui.link}>+ Add {fitments.length ? 'another ' : ''}vehicle</Text>
            </TouchableOpacity>
          ) : null}
          <TouchableOpacity
            onPress={async () => {
              const rows = await MarketApi.lastFitment(listingId).catch(() => []);
              if (rows.length === 0) Alert.alert('Nothing to copy', 'Your earlier listings have no fitment yet.');
              else update({ fitments: rows });
            }}
          >
            <Text style={ui.link}>⧉ Copy fitment from my last listing</Text>
          </TouchableOpacity>
        </View>
        <FieldError messages={errorsFor('fitments')} />
        <MakeModelPicker
          visible={typeof vehiclePicker === 'number'}
          country={country}
          onClose={() => setVehiclePicker(null)}
          onPick={(make, model) => {
            if (typeof vehiclePicker === 'number') setFitment(vehiclePicker, { make: make.name, makeId: make.id, model: model.name, modelId: model.id });
            setVehiclePicker(null);
          }}
        />
      </View>
    );
  };

  const oemNumbers = (form.partNumbers ?? []).filter(n => n.kind !== 'Aftermarket').map(n => n.value).join(', ');
  const aftermarketNumbers = (form.partNumbers ?? []).filter(n => n.kind === 'Aftermarket').map(n => n.value).join(', ');
  const setNumbers = (oem: string, aftermarket: string) =>
    update({
      partNumbers: [
        ...oem.split(',').map(v => v.trim()).filter(Boolean).map(value => ({ value, kind: 'OEM' as const })),
        ...aftermarket.split(',').map(v => v.trim()).filter(Boolean).map(value => ({ value, kind: 'Aftermarket' as const })),
      ],
    });

  const renderDetails = () => (
    <View>
      <SectionTitle title="Part details" />
      <ChipGroup label="Brand type" options={['GenuineOem', 'OemEquivalent', 'Aftermarket']} value={details.brandType ?? null}
        onChange={v => setDetails({ brandType: v })} />
      <FieldError messages={errorsFor('brandType')} />
      {details.brandType && details.brandType !== 'GenuineOem' ? (
        <>
          <Field label="Brand" placeholder="Bosch, Denso, TYC…" value={details.brand ?? ''} onChangeText={v => setDetails({ brand: v })} />
          <FieldError messages={errorsFor('brand')} />
        </>
      ) : null}
      <Field label="OEM part number(s)" optional placeholder="52119-0K935 (comma-separated)" autoCapitalize="characters"
        value={oemNumbers} onChangeText={v => setNumbers(v, aftermarketNumbers)} />
      <Field label="Aftermarket / interchange numbers" optional autoCapitalize="characters" value={aftermarketNumbers}
        onChangeText={v => setNumbers(oemNumbers, v)} />
      {partType?.hasPosition ? (
        <>
          <ChipGroup label="Position" options={['Front', 'Rear']} value={details.position ?? null} onChange={v => setDetails({ position: v })} />
          <FieldError messages={errorsFor('position')} />
        </>
      ) : null}
      {partType?.hasSide ? (
        <>
          <ChipGroup label="Side" options={['Left', 'Right', 'Centre']} value={details.side ?? null} onChange={v => setDetails({ side: v })} />
          <Text style={ui.hint}>Left / right as seen from the driver's seat.</Text>
          <FieldError messages={errorsFor('side')} />
        </>
      ) : null}
      <ChipGroup label="Condition" options={['New', 'Used', 'Refurbished', 'ForRepair']} value={details.condition ?? null}
        onChange={v => setDetails({ condition: v, usedGrade: v === 'Used' ? details.usedGrade : null })} />
      <FieldError messages={errorsFor('condition')} />
      {details.condition === 'Used' ? (
        <>
          <ChipGroup
            label="Used grade"
            options={[
              { value: 'A', label: 'A', hint: 'A – very little or no damage' },
              { value: 'B', label: 'B', hint: 'B – minor damage' },
              { value: 'C', label: 'C', hint: 'C – noticeable damage' },
            ]}
            value={details.usedGrade ?? null}
            onChange={v => setDetails({ usedGrade: v })}
          />
          <FieldError messages={errorsFor('usedGrade')} />
          {kind === 'VehicleSpecificPart' ? (
            <Field label={`Donor vehicle mileage (${unit})`} optional keyboardType="number-pad" value={details.donorMileage ? String(details.donorMileage) : ''}
              onChangeText={v => setDetails({ donorMileage: parseInt(v, 10) || null })} />
          ) : null}
        </>
      ) : null}
      {partType?.isBodyPart ? (
        <View style={styles.twoCols}>
          <View style={styles.col}>
            <Field label="Colour" optional value={details.colour ?? ''} onChangeText={v => setDetails({ colour: v })} />
          </View>
          <View style={styles.col}>
            <Field label="Paint code" optional autoCapitalize="characters" value={details.paintCode ?? ''} onChangeText={v => setDetails({ paintCode: v })} />
          </View>
        </View>
      ) : null}
      <Field label="Quantity" keyboardType="number-pad" value={String(details.quantity ?? 1)}
        onChangeText={v => setDetails({ quantity: parseInt(v, 10) || 1 })} />
      <Field label="What's included" optional placeholder="with bulbs and wiring, set of 4…" value={details.includes ?? ''}
        onChangeText={v => setDetails({ includes: v })} />
      <Field label="Warranty / returns" optional placeholder="7-day return, 3-month warranty…" value={details.warranty ?? ''}
        onChangeText={v => setDetails({ warranty: v })} />
      {renderDescriptionAndTitle()}
    </View>
  );

  const renderDescriptionAndTitle = () => (
    <>
      <Field label="Description" optional multiline style={styles.multiline} value={form.description ?? ''}
        onChangeText={v => update({ description: v })} placeholder="Anything a buyer should know (no phone numbers or e-mails)" />
      <FieldError messages={errorsFor('description')} />
      <View style={ui.card}>
        <Text style={ui.label}>TITLE (GENERATED)</Text>
        <Text style={styles.titlePreview}>{listing.title}</Text>
        <Field label="Add words" optional maxLength={80} value={form.titleExtra ?? ''} onChangeText={v => update({ titleExtra: v })}
          placeholder="e.g. with fog lamp holes" />
        <Text style={ui.hint}>The generated part stays; your words are added at the end.</Text>
      </View>
    </>
  );

  const renderIdentity = () => (
    <View>
      <SectionTitle title="The vehicle" hint="The VIN / chassis number is kept private – buyers only see the last 6 characters." />
      <PickerField label="Make & model" value={form.make ? `${form.make} ${form.model}` : null} placeholder="Choose make and model"
        onPress={() => setVehiclePicker('identity')} />
      <FieldError messages={errorsFor('make', 'model')} />
      <View style={styles.twoCols}>
        <View style={styles.col}>
          <Field label="Year" keyboardType="number-pad" maxLength={4} value={form.year ? String(form.year) : ''}
            onChangeText={v => update({ year: parseInt(v, 10) || undefined })} />
        </View>
        <View style={styles.col}>
          <Field label="Trim / variant" optional value={vehicle.trim ?? ''} onChangeText={v => setVehicle({ trim: v })} />
        </View>
      </View>
      <FieldError messages={errorsFor('year')} />
      <ChipGroup label="Body type" optional options={BODY_TYPES} value={vehicle.bodyType ?? null} onChange={v => setVehicle({ bodyType: v })} clearable />
      <ChipGroup label="Fuel" options={FUELS} value={vehicle.fuel ?? null} onChange={v => setVehicle({ fuel: v })} />
      <FieldError messages={errorsFor('fuel')} />
      <Field label="Engine size" optional placeholder="1.2, 2.8 …" value={vehicle.engineSize ?? ''} onChangeText={v => setVehicle({ engineSize: v })} suffix="L" />
      <ChipGroup label="Transmission" options={TRANSMISSIONS} value={vehicle.transmission ?? null} onChange={v => setVehicle({ transmission: v })} />
      <FieldError messages={errorsFor('transmission')} />
      <ChipGroup label="Drivetrain" optional options={DRIVETRAINS} value={vehicle.drivetrain ?? null} onChange={v => setVehicle({ drivetrain: v })} clearable />
      <ChipGroup label="Steering side" optional options={['LHD', 'RHD']} value={vehicle.steeringSide ?? null} onChange={v => setVehicle({ steeringSide: v })} clearable />
      <Field label="Colour" optional value={form.colour ?? ''} onChangeText={v => update({ colour: v })} />
      <Field label="VIN / chassis number" optional autoCapitalize="characters" value={vehicle.vin ?? ''} onChangeText={v => setVehicle({ vin: v })} />
      <FieldError messages={errorsFor('vin')} />
      {renderDescriptionAndTitle()}
      <MakeModelPicker
        visible={vehiclePicker === 'identity'}
        country={country}
        onClose={() => setVehiclePicker(null)}
        onPick={(make, model) => {
          update({ make: make.name, model: model.name, vehicle: { ...vehicle, bodyType: vehicle.bodyType ?? model.bodyType ?? null } });
          setVehiclePicker(null);
        }}
      />
    </View>
  );

  const renderCondition = () => (
    <View>
      <SectionTitle title="Condition" />
      <ChipGroup label="Running status" options={['RunsAndDrives', 'RunsNotDrivable', 'DoesNotRun']} value={vehicle.runningStatus ?? null}
        onChange={v => setVehicle({ runningStatus: v })} />
      <FieldError messages={errorsFor('runningStatus')} />
      <Field label={`Mileage (${unit})`} keyboardType="number-pad" value={form.mileage != null ? String(form.mileage) : ''}
        onChangeText={v => update({ mileage: v === '' ? undefined : parseInt(v, 10) || 0 })} suffix={unit} />
      <FieldError messages={errorsFor('mileage')} />
      <ChipGroup label="Damage" options={['None', 'Accident', 'Flood', 'Fire', 'Mechanical']} value={vehicle.damageType ?? null}
        onChange={v => setVehicle({ damageType: v, damageAreas: v === 'None' ? [] : vehicle.damageAreas })} />
      <FieldError messages={errorsFor('damageType')} />
      {vehicle.damageType && vehicle.damageType !== 'None' ? (
        <ChipGroup label="Damage areas" multi options={['Front', 'Rear', 'Left', 'Right', 'Roof', 'Underbody']} value={vehicle.damageAreas}
          onChange={v => setVehicle({ damageAreas: v })} />
      ) : null}
      <View style={styles.twoCols}>
        <View style={styles.col}>
          <Field label="Keys" optional keyboardType="number-pad" value={vehicle.keys != null ? String(vehicle.keys) : ''}
            onChangeText={v => setVehicle({ keys: v === '' ? null : parseInt(v, 10) || 0 })} />
        </View>
        <View style={styles.col}>
          <Field label="Owners" optional keyboardType="number-pad" value={vehicle.owners != null ? String(vehicle.owners) : ''}
            onChangeText={v => setVehicle({ owners: v === '' ? null : parseInt(v, 10) || 0 })} />
        </View>
      </View>
      <ChipGroup label="Service history" optional options={['Full', 'Partial', 'None']} value={vehicle.serviceHistory ?? null}
        onChange={v => setVehicle({ serviceHistory: v })} clearable />
      <View style={[styles.rowBetween, styles.switchRow]}>
        <Text style={styles.switchLabel}>Inspection allowed before bidding</Text>
        <Switch value={vehicle.inspectionAllowed} onValueChange={v => setVehicle({ inspectionAllowed: v })}
          trackColor={{ false: COLORS.border, true: COLORS.primary }} />
      </View>
    </View>
  );

  const renderPapers = () => (
    <View>
      <SectionTitle title={`Papers (${REGIONS[country]?.name ?? country})`} hint="Required for vehicles listed in this country." />
      {(PAPERS[country] ?? []).map(p =>
        p.options ? (
          <ChipGroup key={p.key} label={p.label} options={p.options} value={vehicle.papers[p.key] ?? null}
            onChange={v => setVehicle({ papers: { ...vehicle.papers, [p.key]: v } })} />
        ) : (
          <Field key={p.key} label={p.label} placeholder={p.placeholder} value={vehicle.papers[p.key] ?? ''}
            onChangeText={v => setVehicle({ papers: { ...vehicle.papers, [p.key]: v } })} />
        ),
      )}
      <FieldError messages={errorsFor('papers')} />
    </View>
  );

  const renderLocation = () => {
    const options = form.deliveryOptions ?? [];
    const pickup = options.includes('Pickup');
    const shipping = options.includes('Shipping');
    const toggle = (o: DeliveryOption) =>
      update({ deliveryOptions: options.includes(o) ? options.filter(x => x !== o) : [...options, o] });
    return (
      <View>
        <SectionTitle title="Where is it?" hint="Defaults to your location (Home → 📍). You can change the area name." />
        <View style={ui.card}>
          <Text style={styles.locationText}>📍 {form.locationName || 'No location yet'}</Text>
          {location ? (
            <TouchableOpacity
              onPress={() => update({
                latitude: location.latitude, longitude: location.longitude,
                locationName: [location.area, location.city].filter(Boolean).join(', ') || form.locationName,
                countryCode: location.countryCode ?? form.countryCode,
              })}
            >
              <Text style={[ui.link, styles.topGap]}>
                Use my location{location.city ? ` (${[location.area, location.city].filter(Boolean).join(', ')})` : ''}
              </Text>
            </TouchableOpacity>
          ) : (
            <Text style={ui.hint}>Turn on location so buyers nearby can find it.</Text>
          )}
        </View>
        <Field label="Area / city" value={form.locationName ?? ''} onChangeText={v => update({ locationName: v })} placeholder="Kukatpally, Hyderabad" />
        <FieldError messages={errorsFor('location', 'countryCode')} />
        <View style={ui.field}>
          <Text style={ui.label}>HOW CAN THE BUYER GET IT? (AT LEAST ONE)</Text>
          <View style={styles.deliveryRow}>
            <CheckboxRow checked={pickup} onToggle={() => toggle('Pickup')} label="Pickup" />
            <CheckboxRow checked={shipping} onToggle={() => toggle('Shipping')} label="Shipping" />
          </View>
        </View>
        <FieldError messages={errorsFor('deliveryOptions')} />
        {pickup ? (
          <>
            <Text style={ui.hint}>
              Pickup is at the item location – buyers see the city{form.locationName ? ` (${form.locationName.split(',').pop()?.trim()})` : ''}.
            </Text>
            <Field label="Exact pickup address" optional placeholder="Plot 12, Road 4, KPHB Colony" value={form.pickupAddress ?? ''}
              onChangeText={v => update({ pickupAddress: v })} maxLength={300} />
            <Text style={ui.hint}>Only shared with the buyer after the sale.</Text>
            <FieldError messages={errorsFor('pickupAddress')} />
          </>
        ) : null}
        {shipping ? (
          <>
            <View style={[styles.rowBetween, styles.switchRow]}>
              <Text style={styles.switchLabel}>Free shipping</Text>
              <Switch value={form.shippingFee === 0} onValueChange={v => update({ freeShipping: v, shippingFee: v ? 0 : undefined })}
                trackColor={{ false: COLORS.border, true: COLORS.primary }} accessibilityLabel="Free shipping" />
            </View>
            {form.shippingFee !== 0 ? (
              <Field label={`Shipping fee (${REGIONS[country]?.currencyCode ?? ''})`} keyboardType="number-pad" suffix={currency}
                value={form.shippingFee ? String(form.shippingFee) : ''} placeholder="e.g. 500"
                onChangeText={v => update({ freeShipping: false, shippingFee: parseInt(v.replace(/[^0-9]/g, ''), 10) || undefined })} />
            ) : null}
            <FieldError messages={errorsFor('shippingFee')} />
            <View style={ui.field}>
              <Text style={ui.label}>SHIPS WITHIN</Text>
              <Text style={styles.locationText}>{REGIONS[country]?.name ?? country}</Text>
              <Text style={ui.hint}>Shipping is within your country only for now.</Text>
            </View>
            <ChipGroup
              label="Size class"
              options={[
                { value: 'Small', hint: 'Small – fits in a parcel' },
                { value: 'Medium', hint: 'Medium – a large box' },
                { value: 'Large', hint: 'Large – bumper, door, bonnet' },
                { value: 'Freight', hint: 'Freight – engine, gearbox' },
              ]}
              value={form.sizeClass ?? null}
              onChange={v => update({ sizeClass: v })}
            />
            <FieldError messages={errorsFor('sizeClass')} />
          </>
        ) : null}
      </View>
    );
  };

  const isFixed = form.saleType === 'FixedPrice';
  const end = plannedEnd(form);
  const units = kind === 'Vehicle' ? 1 : details.quantity ?? 1;
  const renderAuction = () => (
    <View>
      <SectionTitle title="How do you want to sell it?" />
      <ChipGroup
        options={[
          { value: 'Auction', label: 'Auction', hint: 'Buyers bid on top of your price; the highest total when time runs out wins.' },
          { value: 'FixedPrice', label: 'Direct sale', hint: 'Buyers pay your price straight away. It stays listed until it is sold out or you cancel it.' },
        ]}
        value={form.saleType ?? 'Auction'}
        onChange={v => update({ saleType: v })}
      />
      <FieldError messages={errorsFor('saleType')} />

      <Field
        label={`${isFixed ? (units > 1 ? 'Price per unit' : 'Price') : 'Part price'} (${REGIONS[country]?.currencyCode ?? ''})`}
        keyboardType="number-pad"
        value={form.partPrice ? String(form.partPrice) : ''} onChangeText={v => update({ partPrice: parseInt(v.replace(/[^0-9]/g, ''), 10) || undefined })}
        suffix={currency} />
      <FieldError messages={errorsFor('partPrice')} />

      {isFixed ? (
        <>
          {kind === 'Vehicle' ? (
            <Text style={ui.hint}>One vehicle – the first buyer gets it.</Text>
          ) : (
            <>
              <Field label="Units for sale" keyboardType="number-pad" value={String(units)}
                onChangeText={v => setDetails({ quantity: parseInt(v, 10) || 1 })} />
              <Text style={ui.hint}>
                Buyers choose how many they want{units > 1 && form.partPrice ? ` – all ${units} for ${money(form.partPrice * units)}` : ''}.
              </Text>
              <FieldError messages={errorsFor('quantity')} />
            </>
          )}
        </>
      ) : (
        <>
          <PickerField label="Auction ends" value={formatEnd(end)} placeholder="Choose the date and time" onPress={() => setEndPicker(true)} />
          {endProblem(end) ? (
            <FieldError messages={[endProblem(end)!]} />
          ) : (
            <Text style={ui.hint}>
              Live for <Countdown end={end.toISOString()} style={styles.countdownHint} /> (hh:mm:ss) if you publish now.
            </Text>
          )}
          <FieldError messages={errorsFor('auctionEnd', 'durationDays')} />
          <EndTimePicker
            visible={endPicker}
            value={end}
            onClose={() => setEndPicker(false)}
            onPick={d => {
              setEndPicker(false);
              update({ auctionEnd: d.toISOString(), durationDays: undefined });
            }}
          />
          <View style={ui.card}>
            <Text style={ui.label}>HOW BIDS GO UP (SET BY SHOPCARPARTS)</Text>
            {bidSteps.map(s => (
              <View key={s.from} style={styles.rowBetween}>
                <Text style={styles.stepText}>{s.to == null ? `${money(s.from)} and above` : s.from === 0 ? `below ${money(s.to + 1)}` : `${money(s.from)} – ${money(s.to)}`}</Text>
                <Text style={styles.stepValue}>+ {money(s.step)}</Text>
              </View>
            ))}
            <Text style={ui.hint}>No reserve price. Bids can't be withdrawn.</Text>
          </View>
        </>
      )}
    </View>
  );

  const renderReview = () => {
    const photos = listing.imageItems.length;
    const limits = kind === 'Vehicle' ? PHOTO_LIMITS.vehicle : PHOTO_LIMITS.part;
    const rows: [string, string | undefined | null][] = [
      ['Title', listing.title],
      ['Kind', label(kind)],
      kind === 'Vehicle' ? ['Vehicle', `${form.year ?? ''} ${form.make ?? ''} ${form.model ?? ''}`] : ['Part type', partType?.name],
      ['Fitment', listing.fitmentSummary],
      ['Condition', [label(details.condition), details.usedGrade ? `Grade ${details.usedGrade}` : null].filter(Boolean).join(' · ') || null],
      ['Photos', `${photos} (${limits.min}–${limits.max})`],
      ['Location', form.locationName],
      ['Pickup', (form.deliveryOptions ?? []).includes('Pickup') ? `Yes${form.locationName ? ` – ${form.locationName}` : ''}` : 'No'],
      ['Shipping', (form.deliveryOptions ?? []).includes('Shipping')
        ? [shippingFeeText(form.shippingFee, money) || 'Fee missing', form.sizeClass, `within ${REGIONS[country]?.name ?? country}`].filter(Boolean).join(' · ')
        : 'No'],
      ['Selling', isFixed ? 'Direct sale' : 'Auction'],
      [isFixed ? 'Price' : 'Part price', form.partPrice ? money(form.partPrice) : null],
      isFixed ? ['Units for sale', String(units)] : ['Ends', formatEnd(end)],
    ];
    return (
      <View>
        <SectionTitle title="Check and publish" />
        <View style={ui.card}>
          {rows.filter(([, v]) => v).map(([k, v]) => (
            <View key={k} style={styles.reviewRow}>
              <Text style={styles.reviewKey}>{k}</Text>
              <Text style={styles.reviewValue}>{v}</Text>
            </View>
          ))}
        </View>
        {Object.keys(errors).length > 0 ? (
          <View style={ui.card}>
            <Text style={styles.errorTitle}>Please fix these first</Text>
            {steps.filter(stepHasErrors).map(s => (
              <TouchableOpacity key={s} onPress={() => setStepIndex(steps.indexOf(s))}>
                <Text style={styles.fixLink}>• {STEP_TITLES[s]}: {Object.entries(errors).filter(([f]) => FIELD_STEP[f] === s).flatMap(([, m]) => m).join(' ')} ›</Text>
              </TouchableOpacity>
            ))}
          </View>
        ) : null}
        <PrimaryButton label={publishing ? 'Publishing…' : 'Publish listing'} onPress={publish} loading={publishing} style={styles.topGap} />
      </View>
    );
  };

  const renderPhotos = () => (
    <PhotosStep listing={listing} errors={errors.photos} onChange={imageItems => setListing(l => (l ? { ...l, imageItems } : l))} />
  );

  const content: Record<StepKey, () => React.ReactNode> = {
    partType: renderPartType,
    specs: renderSpecs,
    fitment: renderFitment,
    details: renderDetails,
    identity: renderIdentity,
    condition: renderCondition,
    papers: renderPapers,
    photos: renderPhotos,
    location: renderLocation,
    auction: renderAuction,
    review: renderReview,
  };

  const saveLabel = status === 'saving' ? 'Saving…' : status === 'saved' ? 'Draft saved ✓' : status === 'error' ? 'Not saved' : '';

  return (
    <SafeAreaView style={styles.container}>
      <Header
        title={listing.status === 'Draft' ? `Sell – ${label(kind)}` : 'Edit listing'}
        subtitle={`Step ${stepIndex + 1} of ${steps.length}: ${STEP_TITLES[step]}${saveLabel ? ` · ${saveLabel}` : ''}`}
        onBack={async () => {
          await save();
          navigation.goBack();
        }}
      />
      <View style={styles.progress}>
        {steps.map((s, i) => (
          <TouchableOpacity key={s} style={[styles.dot, i <= stepIndex && styles.dotDone, stepHasErrors(s) && styles.dotError]} onPress={() => goTo(i)} />
        ))}
      </View>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.pad} keyboardShouldPersistTaps="handled">
          {content[step]()}
        </ScrollView>
      </KeyboardAvoidingView>
      {step !== 'review' ? (
        <View style={styles.footer}>
          {stepIndex > 0 ? (
            <TouchableOpacity style={styles.backBtn} onPress={() => goTo(stepIndex - 1)}>
              <Text style={styles.backText}>Back</Text>
            </TouchableOpacity>
          ) : null}
          <TouchableOpacity style={styles.nextBtn} onPress={() => goTo(stepIndex + 1)} disabled={step === 'partType' && !form.partTypeId && kind !== 'Vehicle'}>
            <Text style={styles.nextText}>Next: {STEP_TITLES[steps[stepIndex + 1]]}</Text>
          </TouchableOpacity>
        </View>
      ) : null}
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  flex: { flex: 1 },
  loader: { marginTop: SPACING.xxl },
  pad: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  progress: { flexDirection: 'row', gap: 4, paddingHorizontal: SPACING.md, paddingTop: SPACING.sm },
  dot: { flex: 1, height: 5, borderRadius: 3, backgroundColor: COLORS.border },
  dotDone: { backgroundColor: COLORS.primary },
  dotError: { backgroundColor: COLORS.danger },
  rowBetween: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  twoCols: { flexDirection: 'row', gap: SPACING.sm },
  col: { flex: 1 },
  cardTitle: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  remove: { color: COLORS.danger, fontSize: 13, fontWeight: '700' },
  linksRow: { marginTop: SPACING.md, gap: SPACING.md },
  multiline: { minHeight: 90, textAlignVertical: 'top', paddingTop: SPACING.sm },
  titlePreview: { color: COLORS.text, fontSize: 15, fontWeight: '800', lineHeight: 21 },
  switchRow: { marginTop: SPACING.md },
  switchLabel: { color: COLORS.textSecondary, fontSize: 14, fontWeight: '600', flex: 1 },
  locationText: { color: COLORS.text, fontSize: 15, fontWeight: '700' },
  deliveryRow: { flexDirection: 'row', gap: SPACING.xl },
  topGap: { marginTop: SPACING.md },
  stepText: { color: COLORS.textSecondary, fontSize: 13, paddingVertical: 3 },
  stepValue: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '800' },
  countdownHint: { color: COLORS.text, fontWeight: '800', fontVariant: ['tabular-nums'] },
  reviewRow: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: COLORS.border, gap: SPACING.md },
  reviewKey: { color: COLORS.textMuted, fontSize: 13 },
  reviewValue: { color: COLORS.text, fontSize: 13, fontWeight: '700', flexShrink: 1, textAlign: 'right' },
  errorTitle: { color: COLORS.danger, fontSize: 14, fontWeight: '800', marginBottom: SPACING.xs },
  fixLink: { color: COLORS.textSecondary, fontSize: 13, paddingVertical: 4 },
  footer: { flexDirection: 'row', gap: SPACING.sm, padding: SPACING.md, borderTopWidth: 1, borderTopColor: COLORS.border, backgroundColor: COLORS.surface },
  backBtn: { paddingVertical: 14, paddingHorizontal: SPACING.lg, borderRadius: RADIUS.md, backgroundColor: COLORS.cardSecondary },
  backText: { color: COLORS.text, fontWeight: '700', fontSize: 14 },
  nextBtn: { flex: 1, paddingVertical: 14, borderRadius: RADIUS.md, backgroundColor: COLORS.primary, alignItems: 'center' },
  nextText: { color: COLORS.text, fontWeight: '800', fontSize: 14 },
});
