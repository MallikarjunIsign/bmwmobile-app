import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, TextInput, Modal, FlatList, ActivityIndicator, TextInputProps } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { MarketApi } from '../../services/marketApi';
import type { Category, Make, PartType, VehicleModel } from '../../types/listing';
import { label as niceLabel } from '../../utils/time';

export const SectionTitle: React.FC<{ title: string; hint?: string }> = ({ title, hint }) => (
  <View style={styles.sectionHead}>
    <Text style={styles.sectionTitle}>{title}</Text>
    {hint ? <Text style={styles.hint}>{hint}</Text> : null}
  </View>
);

export const FieldError: React.FC<{ messages?: string[] }> = ({ messages }) =>
  messages && messages.length > 0 ? <Text style={styles.error}>{messages.join('\n')}</Text> : null;

interface FieldProps extends TextInputProps {
  label: string;
  optional?: boolean;
  suffix?: string;
}

/** A labelled text input in the app's dark style. */
export const Field: React.FC<FieldProps> = ({ label, optional, suffix, style, ...rest }) => (
  <View style={styles.field}>
    <Text style={styles.label}>
      {label.toUpperCase()}
      {optional ? <Text style={styles.optional}> (OPTIONAL)</Text> : null}
    </Text>
    <View style={styles.inputRow}>
      <TextInput style={[styles.input, style]} placeholderTextColor={COLORS.textMuted} accessibilityLabel={label} {...rest} />
      {suffix ? <Text style={styles.suffix}>{suffix}</Text> : null}
    </View>
  </View>
);

export interface ChipOption {
  value: string;
  label?: string;
  hint?: string;
}

interface ChipGroupProps {
  label?: string;
  optional?: boolean;
  options: (string | ChipOption)[];
  value: string | string[] | null | undefined;
  onChange: (value: any) => void;
  multi?: boolean;
  /** Tapping the selected chip again clears it (single choice). */
  clearable?: boolean;
}

/** Choice chips (single or multiple). */
export const ChipGroup: React.FC<ChipGroupProps> = ({ label, optional, options, value, onChange, multi, clearable }) => {
  const opts = options.map(o => (typeof o === 'string' ? { value: o } : o));
  const selected = (v: string) => (Array.isArray(value) ? value.includes(v) : value === v);
  const press = (v: string) => {
    if (multi) {
      const list = Array.isArray(value) ? value : [];
      onChange(list.includes(v) ? list.filter(x => x !== v) : [...list, v]);
    } else {
      onChange(clearable && value === v ? null : v);
    }
  };
  const hint = opts.find(o => selected(o.value))?.hint;
  return (
    <View style={styles.field}>
      {label ? (
        <Text style={styles.label}>
          {label.toUpperCase()}
          {optional ? <Text style={styles.optional}> (OPTIONAL)</Text> : null}
        </Text>
      ) : null}
      <View style={styles.chips}>
        {opts.map(o => (
          <TouchableOpacity
            key={o.value}
            style={[styles.chip, selected(o.value) && styles.chipActive]}
            onPress={() => press(o.value)}
            accessibilityState={{ selected: selected(o.value) }}
          >
            <Text style={[styles.chipText, selected(o.value) && styles.chipTextActive]}>{o.label ?? niceLabel(o.value)}</Text>
          </TouchableOpacity>
        ))}
      </View>
      {hint ? <Text style={styles.hint}>{hint}</Text> : null}
    </View>
  );
};

// ── pickers ────────────────────────────────────────────────────────

interface PickerModalProps<T> {
  visible: boolean;
  title: string;
  placeholder: string;
  onClose: () => void;
  load: (q: string) => Promise<T[]>;
  render: (item: T) => { key: string; title: string; subtitle?: string; disabled?: boolean };
  onPick: (item: T) => void;
  /** "Other – request to add": shown with the typed text when nothing fits. */
  onRequest?: (text: string) => Promise<void>;
  header?: React.ReactNode;
}

/** A full-screen search list (search-as-you-type). */
export function PickerModal<T>({ visible, title, placeholder, onClose, load, render, onPick, onRequest, header }: PickerModalProps<T>) {
  const insets = useSafeAreaInsets();
  const [q, setQ] = useState('');
  const [items, setItems] = useState<T[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!visible) return;
    let alive = true;
    setLoading(true);
    const timer = setTimeout(() => {
      load(q.trim())
        .then(list => alive && setItems(list))
        .catch(e => alive && setError(e?.message ?? 'Something went wrong'))
        .finally(() => alive && setLoading(false));
    }, 250);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [q, visible]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={[styles.modal, { paddingTop: insets.top + SPACING.sm, paddingBottom: insets.bottom }]}>
        <View style={styles.modalHead}>
          <Text style={styles.modalTitle}>{title}</Text>
          <TouchableOpacity onPress={onClose} hitSlop={10}>
            <Text style={styles.close}>✕</Text>
          </TouchableOpacity>
        </View>
        <TextInput
          style={styles.search}
          placeholder={placeholder}
          placeholderTextColor={COLORS.textMuted}
          value={q}
          onChangeText={setQ}
          autoFocus
        />
        {header}
        {error ? <Text style={styles.error}>{error}</Text> : null}
        {loading ? <ActivityIndicator color={COLORS.primaryGlow} style={styles.spinner} /> : null}
        <FlatList
          data={items}
          keyboardShouldPersistTaps="handled"
          keyExtractor={item => render(item).key}
          renderItem={({ item }) => {
            const r = render(item);
            return (
              <TouchableOpacity
                style={[styles.row, r.disabled && styles.rowDisabled]}
                onPress={() => !r.disabled && onPick(item)}
                disabled={r.disabled}
              >
                <Text style={styles.rowTitle}>{r.title}</Text>
                {r.subtitle ? <Text style={styles.rowSubtitle}>{r.subtitle}</Text> : null}
              </TouchableOpacity>
            );
          }}
          ListFooterComponent={
            onRequest && q.trim().length >= 2 ? (
              <TouchableOpacity
                style={styles.requestRow}
                onPress={() => onRequest(q.trim()).catch(e => setError(e?.message ?? 'Could not add it'))}
              >
                <Text style={styles.requestText}>➕ Other – request to add “{q.trim()}”</Text>
                <Text style={styles.rowSubtitle}>You can use it on your listing now; an admin approves it for everyone.</Text>
              </TouchableOpacity>
            ) : undefined
          }
        />
      </View>
    </Modal>
  );
}

/** Common parts: only the part types that fit any vehicle, and only the (sub-)categories that still contain some. */
export function commonCategories(categories: Category[]): Category[] {
  return categories
    .map(c => ({
      ...c,
      subCategories: commonCategories(c.subCategories),
      partTypes: c.partTypes.filter(t => t.fitmentMode !== 'VehicleSpecific'),
    }))
    .filter(c => c.subCategories.length + c.partTypes.length > 0);
}

/** Part type: search-as-you-type over names and synonyms, or browse Category > Sub-category > Part type. */
export const PartTypePicker: React.FC<{
  visible: boolean;
  onClose: () => void;
  onPick: (type: PartType) => void;
  /** Common parts: only part types that fit any vehicle (universal or by size) are shown. */
  commonOnly?: boolean;
}> = ({ visible, onClose, onPick, commonOnly }) => {
  const [categories, setCategories] = useState<Category[]>([]);
  const [path, setPath] = useState<Category[]>([]);

  useEffect(() => {
    if (visible && categories.length === 0)
      MarketApi.categories(commonOnly)
        // Also filtered here, in case the server is older and ignores commonOnly.
        .then(list => setCategories(commonOnly ? commonCategories(list) : list))
        .catch(() => undefined);
    if (!visible) setPath([]);
  }, [visible]); // eslint-disable-line react-hooks/exhaustive-deps

  const current = path[path.length - 1];
  const load = async (q: string): Promise<(PartType | Category)[]> => {
    if (q) {
      const types = await MarketApi.partTypes(q, { commonOnly });
      return commonOnly ? types.filter(t => t.fitmentMode !== 'VehicleSpecific') : types;
    }
    if (!current) return categories;
    return [...current.subCategories, ...current.partTypes];
  };
  const isType = (x: PartType | Category): x is PartType => (x as PartType).fitmentMode !== undefined;

  return (
    <PickerModal<PartType | Category>
      key={`${current?.id ?? 'root'}-${categories.length}`}
      visible={visible}
      title={current ? current.name : 'Choose the part type'}
      placeholder="Type a part: bumper, bonnet, tyre, dash cam…"
      onClose={onClose}
      load={load}
      header={
        path.length > 0 ? (
          <TouchableOpacity onPress={() => setPath(p => p.slice(0, -1))} style={styles.back}>
            <Text style={styles.backText}>← {path.length > 1 ? path[path.length - 2].name : 'All categories'}</Text>
          </TouchableOpacity>
        ) : null
      }
      render={x =>
        isType(x)
          ? {
              key: `t${x.id}`,
              title: x.name,
              subtitle: [
                x.matchedSynonym ? `“${x.matchedSynonym}”` : null,
                [x.category, x.subCategory].filter(Boolean).join(' › '),
                niceLabel(x.fitmentMode),
              ].filter(Boolean).join(' · '),
            }
          : { key: `c${x.id}`, title: `${x.name}  ›`, subtitle: `${x.partTypes.length + x.subCategories.length} items` }
      }
      onPick={x => (isType(x) ? onPick(x) : setPath(p => [...p, x]))}
    />
  );
};

/** Make, then model of that make (with "Other – request to add"). */
export const MakeModelPicker: React.FC<{
  visible: boolean;
  country?: string;
  onClose: () => void;
  onPick: (make: Make, model: VehicleModel) => void;
}> = ({ visible, country, onClose, onPick }) => {
  const [make, setMake] = useState<Make | null>(null);
  useEffect(() => {
    if (!visible) setMake(null);
  }, [visible]);

  if (!make) {
    return (
      <PickerModal<Make>
        key="makes"
        visible={visible}
        title="Choose the make"
        placeholder="Toyota, Maruti Suzuki, Ford, Chevy…"
        onClose={onClose}
        load={q => MarketApi.makes(q, country)}
        render={m => ({ key: `m${m.id}`, title: m.name, subtitle: m.isApproved ? undefined : 'Waiting for approval' })}
        onPick={setMake}
        onRequest={async name => setMake(await MarketApi.requestMake(name))}
      />
    );
  }

  return (
    <PickerModal<VehicleModel>
      key={`models-${make.id}`}
      visible={visible}
      title={`${make.name} – choose the model`}
      placeholder="Model"
      onClose={onClose}
      load={q => MarketApi.models(make.id, q, country)}
      header={
        <TouchableOpacity onPress={() => setMake(null)} style={styles.back}>
          <Text style={styles.backText}>← Change make</Text>
        </TouchableOpacity>
      }
      render={m => ({ key: `v${m.id}`, title: m.name, subtitle: m.isApproved ? m.bodyType ?? undefined : 'Waiting for approval' })}
      onPick={m => onPick(make, m)}
      onRequest={async name => onPick(make, await MarketApi.requestModel(make.id, name))}
    />
  );
};

/** A tappable "field" that opens a picker. */
export const PickerField: React.FC<{ label: string; value?: string | null; placeholder: string; onPress: () => void; optional?: boolean }> = ({
  label,
  value,
  placeholder,
  onPress,
  optional,
}) => (
  <View style={styles.field}>
    <Text style={styles.label}>
      {label.toUpperCase()}
      {optional ? <Text style={styles.optional}> (OPTIONAL)</Text> : null}
    </Text>
    <TouchableOpacity style={styles.pickerField} onPress={onPress} activeOpacity={0.8}>
      <Text style={value ? styles.pickerValue : styles.pickerPlaceholder} numberOfLines={1}>{value || placeholder}</Text>
      <Text style={styles.chevron}>▾</Text>
    </TouchableOpacity>
  </View>
);

export const styles = StyleSheet.create({
  sectionHead: {
    marginTop: SPACING.md,
    marginBottom: SPACING.xs,
  },
  sectionTitle: {
    color: COLORS.text,
    fontSize: 17,
    fontWeight: '800',
  },
  hint: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 4,
    lineHeight: 17,
  },
  error: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
    marginTop: SPACING.xs,
  },
  field: {
    marginTop: SPACING.md,
  },
  label: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  optional: {
    color: COLORS.textMuted,
    fontWeight: '600',
  },
  inputRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  input: {
    flex: 1,
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.md,
    color: COLORS.text,
    fontSize: 15,
    paddingHorizontal: SPACING.md,
    minHeight: 48,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  suffix: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '700',
    marginLeft: SPACING.sm,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: SPACING.xs,
  },
  chip: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 4,
    borderRadius: RADIUS.full,
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
    fontWeight: '800',
  },
  modal: {
    flex: 1,
    backgroundColor: COLORS.background,
    paddingHorizontal: SPACING.md,
  },
  modalHead: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: SPACING.sm,
  },
  modalTitle: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    flex: 1,
  },
  close: {
    color: COLORS.textMuted,
    fontSize: 20,
  },
  search: {
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    color: COLORS.text,
    fontSize: 15,
    paddingHorizontal: SPACING.md,
    height: 46,
  },
  spinner: {
    marginTop: SPACING.md,
  },
  row: {
    paddingVertical: SPACING.md - 2,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  rowDisabled: {
    opacity: 0.45,
  },
  rowTitle: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '700',
  },
  rowSubtitle: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  requestRow: {
    paddingVertical: SPACING.md,
  },
  requestText: {
    color: COLORS.primaryGlow,
    fontSize: 15,
    fontWeight: '800',
  },
  back: {
    paddingVertical: SPACING.sm,
  },
  backText: {
    color: COLORS.primaryGlow,
    fontSize: 14,
    fontWeight: '700',
  },
  pickerField: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.md,
    paddingHorizontal: SPACING.md,
    minHeight: 48,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  pickerValue: {
    color: COLORS.text,
    fontSize: 15,
    flex: 1,
  },
  pickerPlaceholder: {
    color: COLORS.textMuted,
    fontSize: 15,
    flex: 1,
  },
  chevron: {
    color: COLORS.primaryGlow,
    fontSize: 14,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    padding: SPACING.md,
    marginTop: SPACING.md,
  },
  link: {
    color: COLORS.primaryGlow,
    fontSize: 14,
    fontWeight: '800',
  },
});
