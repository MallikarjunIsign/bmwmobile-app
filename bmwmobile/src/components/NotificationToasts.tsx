import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Animated, StyleSheet, Text, TouchableOpacity } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { useLiveEvent } from '../services/notifications';
import { useMoney } from '../location/useMoney';
import { useChat } from '../chat/ChatContext';
import type { ChatMessageEvent } from '../types/chat';

/** A personal notification from NotificationService ("Notify"). */
export interface UserNotification {
  type: 'outbid' | 'newBid' | 'endingSoon' | 'won' | 'lost' | 'sold' | 'endedNoBids' | 'customOrderMatched' | 'orderUpdate' | 'chatMessage' | 'reviewRequest';
  auctionId: string;
  title?: string | null;
  amount?: number | null;
  /** orderUpdate: the new tracking step ("Shipped", "Ready for pickup", …). */
  detail?: string | null;
  /** chatMessage: who wrote and which conversation to open. */
  sender?: string;
  conversationId?: string;
}

/** The text of a notification in the user's currency. */
export function notificationText(n: UserNotification, money: (amount: number) => string): { title: string; body: string } {
  const what = n.title ?? 'your listing';
  const amount = n.amount != null ? money(n.amount) : '';
  switch (n.type) {
    case 'outbid': return { title: "You've been outbid", body: `${what} – highest bid now ${amount}. Tap to bid again.` };
    case 'newBid': return { title: 'New bid on your listing', body: `${what} – ${amount}` };
    case 'endingSoon': return { title: 'Auction ending in 1 hour', body: what };
    case 'won': return { title: 'You won! 🎉', body: `${what} – ${amount}. See Orders.` };
    case 'lost': return { title: 'Auction lost', body: `${what} sold for ${amount}.` };
    case 'sold': return { title: 'Sold! 🎉', body: `${what} sold for ${amount}. Hand it over in Orders.` };
    case 'endedNoBids': return { title: 'Ended – no bids', body: `${what}. Relist it with one tap.` };
    case 'reviewRequest': return { title: 'How was your purchase?', body: `Rate ${what} and the seller. Tap to open Orders.` };
    case 'chatMessage': return { title: `New message from ${n.sender ?? 'someone'}`, body: `About ${what}: ${n.detail ?? ''}` };
    case 'orderUpdate': return { title: `Order update: ${n.detail ?? 'updated'}`, body: `${what}. Tap to track it in Orders.` };
    default: return { title: 'A part matching your request is now available', body: what };
  }
}

/**
 * In-app notifications (outbid, new bid, ending in 1 hour, won, lost, sold, ended with no bids, order tracking) as a banner at the
 * top; tapping opens the listing (or Orders / My Listings). Custom-order matches are shown by CustomOrderAlerts.
 */
export function NotificationToasts() {
  const insets = useSafeAreaInsets();
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const money = useMoney();
  const [current, setCurrent] = useState<UserNotification | null>(null);
  const queue = useRef<UserNotification[]>([]);
  const opacity = useRef(new Animated.Value(0)).current;
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const showNext = useCallback(() => {
    const next = queue.current.shift() ?? null;
    setCurrent(next);
    if (!next) return;
    Animated.timing(opacity, { toValue: 1, duration: 200, useNativeDriver: true }).start();
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      Animated.timing(opacity, { toValue: 0, duration: 200, useNativeDriver: true }).start(() => showNext());
    }, 5000);
  }, [opacity]);

  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  const { activeConversation } = useChat();
  // A chat message for a conversation that isn't on screen.
  useLiveEvent('ChatMessage', (m: ChatMessageEvent) => {
    if (!m?.conversationId || activeConversation.current === m.conversationId) return;
    queue.current.push({
      type: 'chatMessage', auctionId: m.listingId, title: m.listingTitle, detail: m.preview, sender: m.sender, conversationId: m.conversationId,
    });
    if (!current) showNext();
  });

  useLiveEvent('Notify', (n: UserNotification) => {
    if (!n?.type || n.type === 'customOrderMatched') return;
    queue.current.push(n);
    if (!current) showNext();
  });

  if (!current) return null;
  const { title, body } = notificationText(current, money);
  const open = () => {
    if (timer.current) clearTimeout(timer.current);
    setCurrent(null);
    if (current.type === 'chatMessage' && current.conversationId) navigation.navigate('Conversation', { conversationId: current.conversationId });
    else if (current.type === 'sold' || current.type === 'orderUpdate' || current.type === 'reviewRequest') navigation.navigate('MainTabs', { screen: 'MyPurchases' });
    else if (current.type === 'won' || current.type === 'lost') navigation.navigate('PartDetails', { auctionId: current.auctionId, showOutcome: true });
    else if (current.type === 'endedNoBids') navigation.navigate('MyListings');
    else navigation.navigate('PartDetails', { auctionId: current.auctionId });
    showNext();
  };

  return (
    <Animated.View style={[styles.wrap, { top: insets.top + SPACING.sm, opacity }]} pointerEvents="box-none">
      <TouchableOpacity style={styles.toast} onPress={open} activeOpacity={0.9} accessibilityRole="alert">
        <Text style={styles.title}>{title}</Text>
        <Text style={styles.body} numberOfLines={2}>{body}</Text>
      </TouchableOpacity>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  wrap: { position: 'absolute', left: SPACING.md, right: SPACING.md, zIndex: 100, elevation: 20 },
  toast: {
    backgroundColor: COLORS.cardSecondary, borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.primaryGlow,
    padding: SPACING.md, shadowColor: '#000', shadowOpacity: 0.4, shadowRadius: 8,
  },
  title: { color: COLORS.text, fontSize: 14, fontWeight: '900' },
  body: { color: COLORS.textSecondary, fontSize: 13, marginTop: 2 },
});
