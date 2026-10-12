import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  View, Text, StyleSheet, FlatList, TouchableOpacity, Image, TextInput, ActivityIndicator, Alert, KeyboardAvoidingView,
  Platform, Modal,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { launchImageLibrary, type Asset } from 'react-native-image-picker';
import type { RootStackParamList } from '../../types/navigation';
import type { ChatMessage, ChatMessageEvent, ChatReceiptEvent, Conversation, ReportReason } from '../../types/chat';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { Header } from '../../components/Header';
import { ChatApi } from '../../services/chatApi';
import { ServerError } from '../../services/api';
import { useLiveEvent } from '../../services/notifications';
import { useChat } from '../../chat/ChatContext';
import { useGarage } from '../../location/GarageContext';
import { useLocations } from '../../location/LocationContext';
import { isCurrentUser } from '../../auth/session';
import { label } from '../../utils/time';
import { PICKER } from '../sell/PhotosStep';
import { SAFETY_TIP, messageTime, moneyIn, quickQuestions, ticks } from '../../chat/chatFormat';
import { ratingText } from '../../utils/rating';

type Props = NativeStackScreenProps<RootStackParamList, 'Conversation'>;

export const MAX_TEXT = 1000;
export const MAX_PHOTOS = 5;

const REASONS: { value: ReportReason; label: string }[] = [
  { value: 'spam', label: 'Spam' },
  { value: 'abuse', label: 'Abuse' },
  { value: 'scam', label: 'Scam' },
  { value: 'other', label: 'Other' },
];

/**
 * One conversation about one listing: the listing header (tap → listing), the safety tip, messages with sent /
 * delivered / read ticks and photos, quick questions for the first message, and Block / Report in the menu.
 * Opened with a conversation id, or a listing id ("Chat with seller") / listing + buyer ("Message buyer").
 */
export const ConversationScreen: React.FC<Props> = ({ route, navigation }) => {
  const { conversationId, listingId, buyer } = route.params;
  const { activeConversation, refreshUnread } = useChat();
  const { selected: garage } = useGarage();
  const { location } = useLocations();
  const [conversation, setConversation] = useState<Conversation | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [photos, setPhotos] = useState<Asset[]>([]);
  const [sending, setSending] = useState(false);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [viewer, setViewer] = useState<string | null>(null);
  const list = useRef<FlatList<ChatMessage>>(null);
  const id = conversation?.id ?? conversationId;

  const markRead = useCallback((cid: string) => {
    ChatApi.read(cid).then(refreshUnread).catch(() => undefined);
  }, [refreshUnread]);

  const load = useCallback(async () => {
    try {
      const c = conversationId ? await ChatApi.get(conversationId) : await ChatApi.start(listingId!, buyer);
      setConversation(c);
      setError(null);
      markRead(c.id);
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't open the chat");
    }
  }, [conversationId, listingId, buyer, markRead]);

  useEffect(() => {
    load();
  }, [load]);

  // While this chat is on screen its new messages are read at once (no badge, no toast).
  useFocusEffect(
    useCallback(() => {
      activeConversation.current = id ?? null;
      return () => {
        activeConversation.current = null;
      };
    }, [id, activeConversation]),
  );

  // A new message here: load the latest page and read it.
  useLiveEvent('ChatMessage', (m: ChatMessageEvent) => {
    if (!id || m?.conversationId !== id) return;
    ChatApi.get(id).then(c => {
      setConversation(prev => (prev ? { ...c, messages: merge(prev.messages, c.messages) } : c));
      markRead(id);
    }).catch(() => undefined);
  });

  // The other side received / read my messages: update the ticks.
  useLiveEvent('ChatReceipt', (r: ChatReceiptEvent) => {
    if (!id || r?.conversationId !== id) return;
    const upTo = new Date(r.upTo).getTime();
    setConversation(prev => prev && {
      ...prev,
      messages: prev.messages.map(m =>
        isCurrentUser(m.sender) && !m.pending && new Date(m.sentAt).getTime() <= upTo && m.state !== 'read'
          ? { ...m, state: r.state === 'Read' ? 'read' : 'delivered' }
          : m),
    });
  });

  const loadOlder = async () => {
    if (!conversation || !conversation.messages.length) return;
    setLoadingOlder(true);
    try {
      const older = await ChatApi.get(conversation.id, conversation.messages[0].sentAt);
      setConversation(c => c && { ...c, hasOlder: older.hasOlder, messages: [...older.messages, ...c.messages] });
    } catch {
      // Keep what's shown.
    } finally {
      setLoadingOlder(false);
    }
  };

  const pickPhotos = async () => {
    const remaining = MAX_PHOTOS - photos.length;
    if (remaining <= 0) return;
    const res = await launchImageLibrary({ ...PICKER, selectionLimit: remaining });
    if (!res.didCancel && res.assets) setPhotos(p => [...p, ...res.assets!.filter(a => a.uri)].slice(0, MAX_PHOTOS));
  };

  const send = async (body = text) => {
    if (!conversation || sending) return;
    const trimmed = body.trim();
    if (!trimmed && photos.length === 0) return;
    const temp: ChatMessage = {
      id: `temp-${Date.now()}`, sender: conversation.role === 'buying' ? conversation.buyer : conversation.seller, text: trimmed || null,
      photos: photos.map(p => ({ url: p.uri! })), sentAt: new Date().toISOString(), state: 'sent', pending: 'sending',
    };
    const attached = photos;
    setConversation(c => c && { ...c, messages: [...c.messages, temp] });
    setText('');
    setPhotos([]);
    setSending(true);
    try {
      const uploaded = attached.length
        ? await ChatApi.uploadPhotos(attached.map(a => ({ uri: a.uri!, type: a.type, fileName: a.fileName })))
        : [];
      const sent = await ChatApi.send(conversation.id, trimmed || undefined, uploaded.map(u => ({ url: u.url, thumbnailUrl: u.thumbnailUrl })));
      setConversation(c => c && { ...c, messages: c.messages.map(m => (m.id === temp.id ? sent : m)) });
    } catch (e) {
      // A timeout doesn't mean it wasn't sent: the server may have saved it. Check before offering to send it again
      // (otherwise the buyer / seller would get it twice).
      const latest = await ChatApi.get(conversation.id).catch(() => null);
      const arrived = latest?.messages.find(m =>
        isCurrentUser(m.sender) && (m.text ?? null) === (temp.text ?? null) && m.photos.length === attached.length &&
        new Date(m.sentAt).getTime() >= new Date(temp.sentAt).getTime() - 60000);
      if (latest && arrived) {
        setConversation(c => (c ? { ...latest, messages: merge(c.messages.filter(m => m.id !== temp.id), latest.messages) } : latest));
        return;
      }
      setConversation(c => c && { ...c, messages: c.messages.filter(m => m.id !== temp.id) });
      setText(trimmed);
      setPhotos(attached);
      Alert.alert('Not sent', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setSending(false);
    }
  };

  const openMenu = () => {
    if (!conversation) return;
    const other = conversation.otherUser;
    Alert.alert(other, undefined, [
      conversation.blockedByMe
        ? { text: `Unblock ${other}`, onPress: () => toggleBlock(false) }
        : { text: `Block ${other}`, style: 'destructive', onPress: confirmBlock },
      { text: 'Report conversation', onPress: chooseReason },
      { text: 'Cancel', style: 'cancel' },
    ]);
  };

  const confirmBlock = () =>
    Alert.alert(`Block ${conversation!.otherUser}?`, "They can't message you or bid on / buy your listings, and you can't message them.", [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Block', style: 'destructive', onPress: () => toggleBlock(true) },
    ]);

  const toggleBlock = async (block: boolean) => {
    try {
      if (block) await ChatApi.block(conversation!.otherUser);
      else await ChatApi.unblock(conversation!.otherUser);
      setConversation(c => c && { ...c, blockedByMe: block });
    } catch (e) {
      Alert.alert('Not changed', e instanceof ServerError ? e.message : 'Please try again.');
    }
  };

  const chooseReason = () =>
    Alert.alert('Report conversation', 'Why are you reporting it?', [
      ...REASONS.map(r => ({ text: r.label, onPress: () => report(r.value) })),
      { text: 'Cancel', style: 'cancel' as const },
    ]);

  const report = async (reason: ReportReason) => {
    try {
      await ChatApi.report(conversation!.id, reason);
      Alert.alert('Thanks for reporting', 'Our team will look at this conversation. You can also block the user.');
    } catch (e) {
      Alert.alert('Not sent', e instanceof ServerError ? e.message : 'Please try again.');
    }
  };

  if (!conversation) {
    return (
      <SafeAreaView style={styles.container}>
        <Header title="Chat" onBack={() => navigation.goBack()} />
        {error ? <Text style={styles.error}>{error}</Text> : <ActivityIndicator color={COLORS.primaryGlow} style={styles.loader} />}
      </SafeAreaView>
    );
  }

  const { listing } = conversation;
  const canSend = !conversation.readOnly && !conversation.blockedByMe && !conversation.blockedMe;
  const firstMessage = conversation.messages.length === 0 && conversation.role === 'buying';

  return (
    <SafeAreaView style={styles.container}>
      <Header
        title={conversation.otherUser}
        subtitle={[conversation.role === 'buying' ? 'Seller' : 'Buyer',
          conversation.otherUserRatingCount ? ratingText(conversation.otherUserRating, conversation.otherUserRatingCount) : null]
          .filter(Boolean).join(' · ')}
        onBack={() => navigation.goBack()}
        rightElement={
          <TouchableOpacity onPress={openMenu} hitSlop={10} accessibilityLabel="Block or report">
            <Text style={styles.menu}>⋮</Text>
          </TouchableOpacity>
        }
      />

      {/* The listing this chat is about – tap to open it. */}
      <TouchableOpacity style={styles.listing} onPress={() => navigation.navigate('PartDetails', { auctionId: listing.id })}
        accessibilityLabel={`Open listing ${listing.title}`}>
        {listing.imageUrl ? <Image source={{ uri: listing.imageUrl }} style={styles.listingThumb} /> : <View style={styles.listingThumb} />}
        <View style={styles.flex}>
          <Text style={styles.listingTitle} numberOfLines={1}>{listing.title || 'Listing'}</Text>
          <Text style={styles.listingMeta}>
            {moneyIn(listing.price, listing.currency)} · {listing.deleted ? 'Removed' : listing.status === 'Live' ? (listing.saleType === 'FixedPrice' ? 'Direct sale' : 'Live auction') : label(listing.status)}
          </Text>
        </View>
        <Text style={styles.chevron}>›</Text>
      </TouchableOpacity>

      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined} keyboardVerticalOffset={0}>
        <FlatList
          ref={list}
          data={conversation.messages}
          keyExtractor={m => m.id}
          contentContainerStyle={styles.messages}
          onContentSizeChange={() => list.current?.scrollToEnd({ animated: false })}
          ListHeaderComponent={
            <>
              {conversation.hasOlder ? (
                <TouchableOpacity onPress={loadOlder} disabled={loadingOlder} style={styles.older}>
                  <Text style={styles.link}>{loadingOlder ? 'Loading…' : 'Show earlier messages'}</Text>
                </TouchableOpacity>
              ) : null}
              {conversation.showSafetyTip ? (
                <View style={styles.tip} accessibilityRole="text">
                  <Text style={styles.tipText}>🛡️ {SAFETY_TIP}</Text>
                </View>
              ) : null}
            </>
          }
          renderItem={({ item }) => {
            const mine = isCurrentUser(item.sender);
            return (
              <View style={[styles.bubbleRow, mine && styles.bubbleRowMine]}>
                <View style={[styles.bubble, mine ? styles.bubbleMine : styles.bubbleTheirs]}>
                  {item.photos.length ? (
                    <View style={styles.photos}>
                      {item.photos.map(p => (
                        <TouchableOpacity key={p.url} onPress={() => setViewer(p.url)} accessibilityLabel="Open photo">
                          <Image source={{ uri: p.thumbnailUrl ?? p.url }} style={styles.photo} />
                        </TouchableOpacity>
                      ))}
                    </View>
                  ) : null}
                  {item.text ? <Text style={styles.messageText}>{item.text}</Text> : null}
                  <Text style={styles.meta}>
                    {messageTime(item.sentAt)}
                    {mine ? (
                      <Text style={item.state === 'read' ? styles.ticksRead : styles.ticks}>
                        {'  '}{item.pending ? '🕓' : ticks(item.state)}
                      </Text>
                    ) : null}
                  </Text>
                </View>
              </View>
            );
          }}
          ListEmptyComponent={
            <Text style={styles.emptyText}>
              {conversation.role === 'buying' ? `Ask ${conversation.otherUser} about this item.` : 'No messages yet.'}
            </Text>
          }
        />

        {canSend && firstMessage ? (
          <View style={styles.chips}>
            {quickQuestions(garage, location?.city).map(q => (
              <TouchableOpacity key={q} style={styles.chip} onPress={() => setText(q)}>
                <Text style={styles.chipText}>{q}</Text>
              </TouchableOpacity>
            ))}
          </View>
        ) : null}

        {canSend ? (
          <View style={styles.composer}>
            {photos.length ? (
              <View style={styles.attached}>
                {photos.map((p, i) => (
                  <TouchableOpacity key={p.uri} onPress={() => setPhotos(list2 => list2.filter((_, n) => n !== i))} accessibilityLabel="Remove photo">
                    <Image source={{ uri: p.uri }} style={styles.attachedPhoto} />
                    <Text style={styles.remove}>✕</Text>
                  </TouchableOpacity>
                ))}
              </View>
            ) : null}
            <View style={styles.inputRow}>
              <TouchableOpacity onPress={pickPhotos} disabled={photos.length >= MAX_PHOTOS} style={styles.attach}
                accessibilityLabel={`Add photos (${photos.length}/${MAX_PHOTOS})`}>
                <Text style={[styles.attachIcon, photos.length >= MAX_PHOTOS && styles.disabled]}>📷</Text>
              </TouchableOpacity>
              <TextInput
                style={styles.input}
                placeholder="Write a message"
                placeholderTextColor={COLORS.textMuted}
                value={text}
                onChangeText={setText}
                multiline
                maxLength={MAX_TEXT}
              />
              <TouchableOpacity style={[styles.send, (!text.trim() && !photos.length) && styles.disabled]} onPress={() => send()}
                disabled={sending || (!text.trim() && !photos.length)} accessibilityLabel="Send">
                <Text style={styles.sendText}>➤</Text>
              </TouchableOpacity>
            </View>
            {text.length > MAX_TEXT - 100 ? <Text style={styles.counter}>{text.length}/{MAX_TEXT}</Text> : null}
          </View>
        ) : (
          <View style={styles.closed}>
            <Text style={styles.closedText}>
              {conversation.readOnly
                ? 'This listing was removed. You can still read the chat.'
                : conversation.blockedByMe
                  ? `You blocked ${conversation.otherUser}. Unblock them in the ⋮ menu to send messages.`
                  : `You can't message ${conversation.otherUser}.`}
            </Text>
          </View>
        )}
      </KeyboardAvoidingView>

      <Modal visible={!!viewer} transparent animationType="fade" onRequestClose={() => setViewer(null)}>
        <TouchableOpacity style={styles.viewer} activeOpacity={1} onPress={() => setViewer(null)} accessibilityLabel="Close photo">
          {viewer ? <Image source={{ uri: viewer }} style={styles.viewerImage} resizeMode="contain" /> : null}
        </TouchableOpacity>
      </Modal>
    </SafeAreaView>
  );
};

/** Older loaded messages + the newest page, without duplicates (the page wins). */
function merge(current: ChatMessage[], latest: ChatMessage[]): ChatMessage[] {
  const ids = new Set(latest.map(m => m.id));
  const first = latest[0]?.sentAt ?? '';
  return [...current.filter(m => !ids.has(m.id) && !m.pending && m.sentAt < first), ...latest];
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  flex: { flex: 1 },
  loader: { marginTop: SPACING.xxl },
  error: { color: COLORS.danger, padding: SPACING.md },
  menu: { color: COLORS.text, fontSize: 22, fontWeight: '900', paddingHorizontal: SPACING.sm },
  listing: {
    flexDirection: 'row', alignItems: 'center', gap: SPACING.sm, padding: SPACING.sm, paddingHorizontal: SPACING.md,
    backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border,
  },
  listingThumb: { width: 44, height: 44, borderRadius: RADIUS.sm, backgroundColor: COLORS.card },
  listingTitle: { color: COLORS.text, fontSize: 13, fontWeight: '800' },
  listingMeta: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '700', marginTop: 2 },
  chevron: { color: COLORS.primaryGlow, fontSize: 22, fontWeight: '700' },
  messages: { padding: SPACING.md, gap: SPACING.sm, flexGrow: 1 },
  older: { alignItems: 'center', paddingVertical: SPACING.sm },
  link: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '700' },
  tip: { backgroundColor: COLORS.badgeBg, borderColor: COLORS.primary, borderWidth: 1, borderRadius: RADIUS.md, padding: SPACING.sm, marginBottom: SPACING.sm },
  tipText: { color: COLORS.badgeText, fontSize: 12, fontWeight: '700', lineHeight: 17 },
  bubbleRow: { flexDirection: 'row', justifyContent: 'flex-start' },
  bubbleRowMine: { justifyContent: 'flex-end' },
  bubble: { maxWidth: '80%', borderRadius: RADIUS.lg, paddingHorizontal: SPACING.md, paddingVertical: SPACING.sm, gap: 4 },
  bubbleMine: { backgroundColor: COLORS.primary, borderBottomRightRadius: 4 },
  bubbleTheirs: { backgroundColor: COLORS.card, borderBottomLeftRadius: 4, borderWidth: 1, borderColor: COLORS.border },
  messageText: { color: COLORS.text, fontSize: 15, lineHeight: 20 },
  meta: { color: COLORS.textSecondary, fontSize: 10, alignSelf: 'flex-end' },
  ticks: { color: COLORS.textSecondary, fontWeight: '800' },
  ticksRead: { color: COLORS.successGlow, fontWeight: '900' },
  photos: { flexDirection: 'row', flexWrap: 'wrap', gap: 4 },
  photo: { width: 110, height: 110, borderRadius: RADIUS.sm, backgroundColor: COLORS.surface },
  emptyText: { color: COLORS.textMuted, fontSize: 13, textAlign: 'center', marginTop: SPACING.xl },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: SPACING.xs, paddingHorizontal: SPACING.md, paddingBottom: SPACING.sm },
  chip: { backgroundColor: COLORS.card, borderColor: COLORS.primaryGlow, borderWidth: 1, borderRadius: RADIUS.full, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2 },
  chipText: { color: COLORS.text, fontSize: 12, fontWeight: '700' },
  composer: { borderTopWidth: 1, borderTopColor: COLORS.border, backgroundColor: COLORS.surface, padding: SPACING.sm },
  attached: { flexDirection: 'row', gap: SPACING.xs, marginBottom: SPACING.xs },
  attachedPhoto: { width: 52, height: 52, borderRadius: RADIUS.sm },
  remove: { position: 'absolute', top: 2, right: 4, color: COLORS.text, fontWeight: '900', fontSize: 12 },
  inputRow: { flexDirection: 'row', alignItems: 'flex-end', gap: SPACING.xs },
  attach: { paddingHorizontal: SPACING.xs, paddingVertical: 10 },
  attachIcon: { fontSize: 22 },
  input: {
    flex: 1, color: COLORS.text, fontSize: 15, backgroundColor: COLORS.card, borderRadius: RADIUS.lg, paddingHorizontal: SPACING.md,
    paddingVertical: 10, maxHeight: 120, borderWidth: 1, borderColor: COLORS.border,
  },
  send: { width: 44, height: 44, borderRadius: 22, backgroundColor: COLORS.primary, alignItems: 'center', justifyContent: 'center' },
  sendText: { color: COLORS.text, fontSize: 18, fontWeight: '900' },
  disabled: { opacity: 0.4 },
  counter: { color: COLORS.textMuted, fontSize: 11, alignSelf: 'flex-end', marginTop: 2 },
  closed: { padding: SPACING.md, borderTopWidth: 1, borderTopColor: COLORS.border, backgroundColor: COLORS.surface },
  closedText: { color: COLORS.textMuted, fontSize: 13, textAlign: 'center' },
  viewer: { flex: 1, backgroundColor: 'rgba(0,0,0,0.92)', justifyContent: 'center' },
  viewerImage: { width: '100%', height: '80%' },
});
