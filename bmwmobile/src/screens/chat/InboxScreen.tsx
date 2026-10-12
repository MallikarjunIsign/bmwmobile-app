import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, FlatList, TouchableOpacity, Image, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../../types/navigation';
import type { ConversationSummary } from '../../types/chat';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { Header } from '../../components/Header';
import { ChatApi } from '../../services/chatApi';
import { ServerError } from '../../services/api';
import { useLiveEvent } from '../../services/notifications';
import { useChat } from '../../chat/ChatContext';
import { inboxTime } from '../../chat/chatFormat';
import { isCurrentUser } from '../../auth/session';
import { ratingText } from '../../utils/rating';

type Props = NativeStackScreenProps<RootStackParamList, 'Inbox'>;

const FILTERS = [
  { value: 'all', label: 'All' },
  { value: 'buying', label: 'Buying' },
  { value: 'selling', label: 'Selling' },
] as const;

/** Messages: conversations newest first – who, which listing, the last message, when, and unread – All / Buying / Selling. */
export const InboxScreen: React.FC<Props> = ({ navigation }) => {
  const { refreshUnread } = useChat();
  const [filter, setFilter] = useState<(typeof FILTERS)[number]['value']>('all');
  const [items, setItems] = useState<ConversationSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setItems(await ChatApi.inbox(filter));
      setError(null);
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't load your messages");
    } finally {
      setLoading(false);
      refreshUnread();
    }
  }, [filter, refreshUnread]);

  useFocusEffect(useCallback(() => { load(); }, [load]));
  useLiveEvent('ChatMessage', () => load());

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Messages" onBack={() => navigation.goBack()} />
      <View style={styles.filters}>
        {FILTERS.map(f => (
          <TouchableOpacity key={f.value} style={[styles.chip, filter === f.value && styles.chipActive]} onPress={() => setFilter(f.value)}
            accessibilityState={{ selected: filter === f.value }}>
            <Text style={[styles.chipText, filter === f.value && styles.chipTextActive]}>{f.label}</Text>
          </TouchableOpacity>
        ))}
      </View>
      <FlatList
        data={items}
        keyExtractor={c => c.id}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={load} tintColor={COLORS.primaryGlow} />}
        contentContainerStyle={styles.list}
        renderItem={({ item }) => {
          const unread = item.unreadCount > 0;
          const mine = item.lastMessageSender ? isCurrentUser(item.lastMessageSender) : false;
          return (
            <TouchableOpacity style={styles.row} onPress={() => navigation.navigate('Conversation', { conversationId: item.id })}
              accessibilityLabel={`Chat with ${item.otherUser} about ${item.listing.title}${unread ? `, ${item.unreadCount} unread` : ''}`}>
              {item.listing.imageUrl ? <Image source={{ uri: item.listing.imageUrl }} style={styles.thumb} /> : <View style={styles.thumb} />}
              <View style={styles.body}>
                <View style={styles.topLine}>
                  <Text style={[styles.user, unread && styles.bold]} numberOfLines={1}>
                    {item.otherUser}
                    <Text style={styles.role}>  · {item.role === 'buying' ? 'Seller' : 'Buyer'}</Text>
                    {item.otherUserRatingCount ? (
                      <Text style={styles.rating}>  {ratingText(item.otherUserRating, item.otherUserRatingCount)}</Text>
                    ) : null}
                  </Text>
                  <Text style={[styles.time, unread && styles.timeUnread]}>{inboxTime(item.lastMessageAt)}</Text>
                </View>
                <Text style={styles.listing} numberOfLines={1}>{item.listing.title}</Text>
                <View style={styles.topLine}>
                  <Text style={[styles.preview, unread && styles.previewUnread]} numberOfLines={1}>
                    {item.blockedByMe ? '🚫 Blocked · ' : ''}{mine ? 'You: ' : ''}{item.lastMessage}
                  </Text>
                  {unread ? (
                    <View style={styles.badge}>
                      <Text style={styles.badgeText}>{item.unreadCount > 99 ? '99+' : item.unreadCount}</Text>
                    </View>
                  ) : null}
                </View>
              </View>
            </TouchableOpacity>
          );
        }}
        ListEmptyComponent={
          loading ? undefined : (
            <Text style={styles.empty}>
              {error ?? (filter === 'selling'
                ? 'No questions from buyers yet.'
                : 'No messages yet. Open a listing and tap “Chat with seller” to ask a question.')}
            </Text>
          )
        }
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  filters: { flexDirection: 'row', gap: SPACING.xs, paddingHorizontal: SPACING.md, paddingVertical: SPACING.sm },
  chip: { backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2, borderRadius: RADIUS.full, borderWidth: 1, borderColor: COLORS.border },
  chipActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  chipText: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '600' },
  chipTextActive: { color: COLORS.text, fontWeight: '800' },
  list: { paddingBottom: SPACING.xxl },
  row: { flexDirection: 'row', gap: SPACING.md, paddingHorizontal: SPACING.md, paddingVertical: SPACING.sm + 2, borderBottomWidth: 1, borderBottomColor: COLORS.border },
  thumb: { width: 56, height: 56, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  body: { flex: 1, gap: 2 },
  topLine: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: SPACING.sm },
  user: { color: COLORS.text, fontSize: 14, fontWeight: '700', flexShrink: 1 },
  bold: { fontWeight: '900' },
  role: { color: COLORS.textMuted, fontSize: 12, fontWeight: '600' },
  rating: { color: COLORS.warningGlow, fontSize: 12, fontWeight: '700' },
  time: { color: COLORS.textMuted, fontSize: 11 },
  timeUnread: { color: COLORS.primaryGlow, fontWeight: '800' },
  listing: { color: COLORS.textSecondary, fontSize: 12 },
  preview: { color: COLORS.textMuted, fontSize: 13, flex: 1 },
  previewUnread: { color: COLORS.text, fontWeight: '700' },
  badge: { minWidth: 20, height: 20, borderRadius: 10, backgroundColor: COLORS.primary, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 5 },
  badgeText: { color: COLORS.text, fontSize: 11, fontWeight: '900' },
  empty: { color: COLORS.textMuted, fontSize: 14, textAlign: 'center', padding: SPACING.xl, lineHeight: 20 },
});
