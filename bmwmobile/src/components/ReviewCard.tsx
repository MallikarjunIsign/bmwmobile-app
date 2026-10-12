import React, { useState } from 'react';
import { View, Text, StyleSheet, Image, TouchableOpacity, Alert, TextInput } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Stars } from './Stars';
import { ReviewApi } from '../services/reviewApi';
import { ServerError } from '../services/api';
import type { Review, ReviewReportReason } from '../types/review';
import { formatWhen } from '../utils/time';

const REASONS: { value: ReviewReportReason; label: string }[] = [
  { value: 'spam', label: 'Spam' },
  { value: 'abuse', label: 'Abusive' },
  { value: 'fake', label: 'Fake review' },
  { value: 'personal', label: 'Personal details' },
  { value: 'other', label: 'Other' },
];

/**
 * One review: stars for the seller and the item, tags, comment, photos, who (partly hidden) and when – optionally the
 * item it was for – and the seller's public reply. The seller can reply once; anyone signed in can report it.
 */
export const ReviewCard: React.FC<{
  review: Review;
  /** Show the item title and photo (seller profile). */
  showItem?: boolean;
  onChange?: (review: Review) => void;
  onOpenPhoto?: (url: string) => void;
}> = ({ review, showItem, onChange, onOpenPhoto }) => {
  const [replying, setReplying] = useState(false);
  const [reply, setReply] = useState('');
  const [busy, setBusy] = useState(false);

  const sendReply = async () => {
    setBusy(true);
    try {
      onChange?.(await ReviewApi.reply(review.id, reply.trim()));
      setReplying(false);
    } catch (e) {
      Alert.alert('Reply not posted', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setBusy(false);
    }
  };

  const report = () =>
    Alert.alert('Report review', 'Why are you reporting it?', [
      ...REASONS.map(r => ({
        text: r.label,
        onPress: () =>
          ReviewApi.report(review.id, r.value)
            .then(() => Alert.alert('Thanks for reporting', 'Our team will check this review.'))
            .catch(e => Alert.alert('Not sent', e instanceof ServerError ? e.message : 'Please try again.')),
      })),
      { text: 'Cancel', style: 'cancel' as const },
    ]);

  const quick = [
    review.communication ? `Communication ${review.communication}★` : null,
    review.accuracy ? `Accuracy ${review.accuracy}★` : null,
    review.delivery ? `Delivery ${review.delivery}★` : null,
  ].filter(Boolean).join(' · ');

  return (
    <View style={styles.card}>
      {showItem ? (
        <View style={styles.item}>
          {review.itemImageUrl ? <Image source={{ uri: review.itemImageUrl }} style={styles.itemThumb} /> : null}
          <Text style={styles.itemTitle} numberOfLines={1}>{review.itemTitle}</Text>
        </View>
      ) : null}
      <View style={styles.head}>
        <Stars value={review.sellerRating} />
        <Text style={styles.who}>{review.isMine ? 'You' : review.buyer} · {formatWhen(review.createdAt)}</Text>
      </View>
      <Text style={styles.meta}>
        Item matched the description: {review.itemRating}★{quick ? ` · ${quick}` : ''} · <Text style={styles.verified}>✓ Verified purchase</Text>
      </Text>
      {review.tags.length ? (
        <View style={styles.tags}>
          {review.tags.map(t => (
            <Text key={t} style={[styles.tag, (t === 'Not as described' || t === 'Slow to respond') && styles.tagBad]}>{t}</Text>
          ))}
        </View>
      ) : null}
      {review.comment ? <Text style={styles.comment}>{review.comment}</Text> : null}
      {review.photos.length ? (
        <View style={styles.photos}>
          {review.photos.map(p => (
            <TouchableOpacity key={p.id} onPress={() => onOpenPhoto?.(p.url)} accessibilityLabel="Open photo">
              <Image source={{ uri: p.thumbnailUrl ?? p.url }} style={styles.photo} />
            </TouchableOpacity>
          ))}
        </View>
      ) : null}

      {review.sellerReply ? (
        <View style={styles.reply}>
          <Text style={styles.replyTitle}>Seller's reply</Text>
          <Text style={styles.comment}>{review.sellerReply}</Text>
        </View>
      ) : null}

      {replying ? (
        <View style={styles.reply}>
          <TextInput style={styles.input} multiline maxLength={1000} value={reply} onChangeText={setReply}
            placeholder="Your public reply (once)" placeholderTextColor={COLORS.textMuted} />
          <View style={styles.actions}>
            <TouchableOpacity onPress={() => setReplying(false)}><Text style={styles.muted}>Cancel</Text></TouchableOpacity>
            <TouchableOpacity onPress={sendReply} disabled={busy || !reply.trim()}>
              <Text style={[styles.link, (busy || !reply.trim()) && styles.disabled]}>Post reply</Text>
            </TouchableOpacity>
          </View>
        </View>
      ) : (
        <View style={styles.actions}>
          {review.canReply ? (
            <TouchableOpacity onPress={() => setReplying(true)}><Text style={styles.link}>↩ Reply</Text></TouchableOpacity>
          ) : <View />}
          {!review.isMine ? (
            <TouchableOpacity onPress={report}><Text style={styles.muted}>Report</Text></TouchableOpacity>
          ) : null}
        </View>
      )}
    </View>
  );
};

const styles = StyleSheet.create({
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.border, padding: SPACING.md, gap: 6, marginBottom: SPACING.sm },
  item: { flexDirection: 'row', alignItems: 'center', gap: SPACING.sm },
  itemThumb: { width: 32, height: 32, borderRadius: RADIUS.sm, backgroundColor: COLORS.card },
  itemTitle: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '700', flex: 1 },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  who: { color: COLORS.textMuted, fontSize: 12 },
  meta: { color: COLORS.textSecondary, fontSize: 12 },
  verified: { color: COLORS.successGlow, fontWeight: '700' },
  tags: { flexDirection: 'row', flexWrap: 'wrap', gap: 4 },
  tag: {
    color: COLORS.successGlow, fontSize: 11, fontWeight: '700', borderWidth: 1, borderColor: COLORS.successGlow, borderRadius: RADIUS.full,
    paddingHorizontal: 8, paddingVertical: 2,
  },
  tagBad: { color: COLORS.warningGlow, borderColor: COLORS.warningGlow },
  comment: { color: COLORS.text, fontSize: 14, lineHeight: 19 },
  photos: { flexDirection: 'row', flexWrap: 'wrap', gap: 4 },
  photo: { width: 64, height: 64, borderRadius: RADIUS.sm, backgroundColor: COLORS.card },
  reply: { backgroundColor: COLORS.card, borderRadius: RADIUS.md, padding: SPACING.sm, gap: 4 },
  replyTitle: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '800' },
  input: { color: COLORS.text, fontSize: 14, minHeight: 60, textAlignVertical: 'top' },
  actions: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  link: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '800' },
  muted: { color: COLORS.textMuted, fontSize: 12, fontWeight: '700' },
  disabled: { opacity: 0.4 },
});
