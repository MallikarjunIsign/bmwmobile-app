import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, TextInput, Image, ActivityIndicator, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { launchImageLibrary } from 'react-native-image-picker';
import type { RootStackParamList } from '../types/navigation';
import { REVIEW_TAGS, type Review, type ReviewPhoto } from '../types/review';
import type { FieldErrors } from '../types/listing';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { StarPicker } from '../components/Stars';
import { ReviewCard } from '../components/ReviewCard';
import { PrimaryButton } from '../components/PrimaryButton';
import { ReviewApi } from '../services/reviewApi';
import { ChatApi } from '../services/chatApi';
import { ServerError } from '../services/api';
import { PICKER } from './sell/PhotosStep';

type Props = NativeStackScreenProps<RootStackParamList, 'WriteReview'>;

export const MAX_COMMENT = 1000;
export const MAX_REVIEW_PHOTOS = 5;

/**
 * "How was your purchase?" – the buyer rates the item (matched the description) and the seller (overall, plus
 * optional communication / accuracy / delivery), picks tags, writes a comment and adds up to 5 photos. Opened with a
 * reviewId it shows the review, editable for 7 days after posting.
 */
export const WriteReviewScreen: React.FC<Props> = ({ route, navigation }) => {
  const { orderId, reviewId, title, imageUrl, seller } = route.params;
  const [existing, setExisting] = useState<Review | null>(null);
  const [editing, setEditing] = useState(!reviewId);
  const [loading, setLoading] = useState(!!reviewId);
  const [itemRating, setItemRating] = useState<number | null>(null);
  const [sellerRating, setSellerRating] = useState<number | null>(null);
  const [communication, setCommunication] = useState<number | null>(null);
  const [accuracy, setAccuracy] = useState<number | null>(null);
  const [delivery, setDelivery] = useState<number | null>(null);
  const [tags, setTags] = useState<string[]>([]);
  const [comment, setComment] = useState('');
  const [photos, setPhotos] = useState<ReviewPhoto[]>([]);
  const [uploading, setUploading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});

  useEffect(() => {
    if (!reviewId) return;
    ReviewApi.get(reviewId)
      .then(r => {
        setExisting(r);
        setItemRating(r.itemRating);
        setSellerRating(r.sellerRating);
        setCommunication(r.communication ?? null);
        setAccuracy(r.accuracy ?? null);
        setDelivery(r.delivery ?? null);
        setTags(r.tags);
        setComment(r.comment ?? '');
        setPhotos(r.photos);
      })
      .catch(e => Alert.alert('Not loaded', e instanceof ServerError ? e.message : 'Please try again.'))
      .finally(() => setLoading(false));
  }, [reviewId]);

  const toggleTag = (t: string) => setTags(list => (list.includes(t) ? list.filter(x => x !== t) : [...list, t]));

  const addPhotos = async () => {
    const remaining = MAX_REVIEW_PHOTOS - photos.length;
    if (remaining <= 0) return;
    const res = await launchImageLibrary({ ...PICKER, selectionLimit: remaining });
    const picked = (res.assets ?? []).filter(a => a.uri);
    if (res.didCancel || picked.length === 0) return;
    setUploading(true);
    try {
      const uploaded = await ChatApi.uploadPhotos(picked.map(a => ({ uri: a.uri!, type: a.type, fileName: a.fileName })), 'review');
      setPhotos(list => [...list, ...uploaded.map(u => ({ id: u.id, url: u.url, thumbnailUrl: u.thumbnailUrl }))].slice(0, MAX_REVIEW_PHOTOS));
    } catch (e) {
      Alert.alert('Photos not added', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setUploading(false);
    }
  };

  const save = async () => {
    if (!itemRating || !sellerRating) {
      setErrors({ ...(!itemRating ? { itemRating: ['Rate the item'] } : {}), ...(!sellerRating ? { sellerRating: ['Rate the seller'] } : {}) });
      return;
    }
    setSaving(true);
    setErrors({});
    const body = {
      orderId, itemRating, sellerRating, communication, accuracy, delivery, tags, comment: comment.trim() || undefined,
      photoIds: photos.map(p => p.id),
    };
    try {
      const saved = existing ? await ReviewApi.update(existing.id, body) : await ReviewApi.create(body);
      setExisting(saved);
      setEditing(false);
      Alert.alert(existing ? 'Review updated' : 'Thanks for your review!', 'It helps other buyers decide.');
    } catch (e) {
      if (e instanceof ServerError && Object.keys(e.fields).length) setErrors(e.fields);
      else Alert.alert('Not saved', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setSaving(false);
    }
  };

  const fieldError = (...keys: string[]) => {
    const messages = keys.flatMap(k => errors[k] ?? []);
    return messages.length ? <Text style={styles.error}>{messages.join(' ')}</Text> : null;
  };

  if (loading) {
    return (
      <SafeAreaView style={styles.container}>
        <Header title="Your review" onBack={() => navigation.goBack()} />
        <ActivityIndicator color={COLORS.primaryGlow} style={styles.loader} />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container}>
      <Header title={existing && !editing ? 'Your review' : 'How was your purchase?'} onBack={() => navigation.goBack()} />
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        <View style={styles.item}>
          {imageUrl ? <Image source={{ uri: imageUrl }} style={styles.thumb} /> : <View style={styles.thumb} />}
          <View style={styles.flex}>
            <Text style={styles.title} numberOfLines={2}>{title}</Text>
            <Text style={styles.seller}>Sold by {seller}</Text>
          </View>
        </View>

        {existing && !editing ? (
          <>
            <ReviewCard review={existing} />
            {existing.canEdit ? (
              <TouchableOpacity onPress={() => setEditing(true)} style={styles.editLink}>
                <Text style={styles.link}>✎ Edit your review</Text>
              </TouchableOpacity>
            ) : (
              <Text style={styles.hint}>Reviews can be edited within 7 days of posting.</Text>
            )}
          </>
        ) : (
          <>
            <StarPicker label="The item" hint="Did it match the description?" value={itemRating} onChange={setItemRating} />
            {fieldError('itemRating')}
            <StarPicker label="The seller" hint="Overall, how was buying from them?" value={sellerRating} onChange={setSellerRating} />
            {fieldError('sellerRating')}

            <Text style={styles.section}>QUICK RATINGS (OPTIONAL)</Text>
            <StarPicker label="Communication" optional value={communication} onChange={setCommunication} />
            <StarPicker label="Accuracy of listing" optional value={accuracy} onChange={setAccuracy} />
            <StarPicker label="Pickup / shipping" optional value={delivery} onChange={setDelivery} />
            {fieldError('communication', 'accuracy', 'delivery')}

            <Text style={styles.section}>TAGS (OPTIONAL)</Text>
            <View style={styles.tags}>
              {REVIEW_TAGS.map(t => (
                <TouchableOpacity key={t} style={[styles.chip, tags.includes(t) && styles.chipActive]} onPress={() => toggleTag(t)}
                  accessibilityState={{ selected: tags.includes(t) }}>
                  <Text style={[styles.chipText, tags.includes(t) && styles.chipTextActive]}>{t}</Text>
                </TouchableOpacity>
              ))}
            </View>
            {fieldError('tags')}

            <Text style={styles.section}>COMMENT (OPTIONAL)</Text>
            <TextInput style={styles.input} multiline maxLength={MAX_COMMENT} value={comment} onChangeText={setComment}
              placeholder="What should other buyers know?" placeholderTextColor={COLORS.textMuted} />
            <Text style={styles.counter}>{comment.length}/{MAX_COMMENT}</Text>
            {fieldError('comment')}

            <Text style={styles.section}>PHOTOS (OPTIONAL, UP TO {MAX_REVIEW_PHOTOS})</Text>
            <View style={styles.photos}>
              {photos.map(p => (
                <TouchableOpacity key={p.id} onPress={() => setPhotos(list => list.filter(x => x.id !== p.id))} accessibilityLabel="Remove photo">
                  <Image source={{ uri: p.thumbnailUrl ?? p.url }} style={styles.photo} />
                  <Text style={styles.remove}>✕</Text>
                </TouchableOpacity>
              ))}
              {photos.length < MAX_REVIEW_PHOTOS ? (
                <TouchableOpacity style={[styles.photo, styles.addPhoto]} onPress={addPhotos} disabled={uploading} accessibilityLabel="Add photos">
                  {uploading ? <ActivityIndicator color={COLORS.primaryGlow} /> : <Text style={styles.addText}>📷 +</Text>}
                </TouchableOpacity>
              ) : null}
            </View>
            {fieldError('photos')}

            <PrimaryButton label={saving ? 'Saving…' : existing ? 'Save changes' : 'Post review'} onPress={save} loading={saving} style={styles.submit} />
          </>
        )}
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  flex: { flex: 1 },
  loader: { marginTop: SPACING.xxl },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  item: { flexDirection: 'row', gap: SPACING.md, alignItems: 'center', backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.sm, borderWidth: 1, borderColor: COLORS.border, marginBottom: SPACING.sm },
  thumb: { width: 56, height: 56, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  title: { color: COLORS.text, fontSize: 14, fontWeight: '800' },
  seller: { color: COLORS.textMuted, fontSize: 12, marginTop: 2 },
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.lg },
  tags: { flexDirection: 'row', flexWrap: 'wrap', gap: SPACING.xs, marginTop: SPACING.xs },
  chip: { backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2, borderRadius: RADIUS.full, borderWidth: 1, borderColor: COLORS.border },
  chipActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  chipText: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '600' },
  chipTextActive: { color: COLORS.text, fontWeight: '800' },
  input: {
    backgroundColor: COLORS.surface, color: COLORS.text, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.border,
    minHeight: 100, padding: SPACING.sm, textAlignVertical: 'top', marginTop: SPACING.xs, fontSize: 15,
  },
  counter: { color: COLORS.textMuted, fontSize: 11, alignSelf: 'flex-end', marginTop: 2 },
  photos: { flexDirection: 'row', flexWrap: 'wrap', gap: SPACING.xs, marginTop: SPACING.xs },
  photo: { width: 72, height: 72, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  addPhoto: { alignItems: 'center', justifyContent: 'center', borderWidth: 1, borderColor: COLORS.primaryGlow, borderStyle: 'dashed' },
  addText: { color: COLORS.primaryGlow, fontWeight: '800' },
  remove: { position: 'absolute', top: 2, right: 6, color: COLORS.text, fontWeight: '900' },
  error: { color: COLORS.danger, fontSize: 12, fontWeight: '600', marginTop: 4 },
  submit: { marginTop: SPACING.lg },
  editLink: { alignSelf: 'flex-start', paddingVertical: SPACING.sm },
  link: { color: COLORS.primaryGlow, fontSize: 14, fontWeight: '800' },
  hint: { color: COLORS.textMuted, fontSize: 12 },
});
