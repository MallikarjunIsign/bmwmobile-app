import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { useAuth } from '../auth/AuthContext';
import { useLiveEvent } from '../services/notifications';
import { ChatApi } from '../services/chatApi';
import { registerForPush } from '../services/push';
import { useLocations } from '../location/LocationContext';
import type { ChatMessageEvent } from '../types/chat';

type ChatValue = {
  /** Unread messages over all conversations (the badge on the Messages icon). */
  unread: number;
  refreshUnread: () => void;
  /** The conversation on screen (its new messages are read at once, so no badge / toast for them). */
  activeConversation: React.MutableRefObject<string | null>;
};

const ChatContext = createContext<ChatValue>({
  unread: 0,
  refreshUnread: () => undefined,
  activeConversation: { current: null },
});

/**
 * Chat state shared by Home (badge), the inbox, the conversation and the toasts. While signed in it registers the
 * phone for push notifications, keeps the unread count current from the live "ChatMessage" events, and confirms
 * delivery of live messages (the sender's second tick).
 */
export function ChatProvider({ children }: { children: React.ReactNode }) {
  const { status, user } = useAuth();
  const { location } = useLocations();
  // Where the phone is, rounded to ~10 km: the phone is registered again when the user moves that far (or changes country).
  const lat = location ? Math.round(location.latitude * 10) / 10 : undefined;
  const lon = location ? Math.round(location.longitude * 10) / 10 : undefined;
  const countryCode = location?.countryCode ?? null;
  const signedIn = status === 'signedIn';
  const [unread, setUnread] = useState(0);
  const activeConversation = useRef<string | null>(null);

  const refreshUnread = useCallback(() => {
    if (!signedIn) return;
    ChatApi.unread().then(setUnread).catch(() => undefined);
  }, [signedIn]);

  useEffect(() => {
    if (!signedIn) {
      setUnread(0);
      return;
    }
    refreshUnread();
    let cleanup: (() => void) | null = null;
    let cancelled = false;
    registerForPush({ latitude: lat, longitude: lon, countryCode }).then(off => (cancelled ? off() : (cleanup = off)));
    // Coming back to the app: messages may have arrived as notifications meanwhile.
    const sub = AppState.addEventListener('change', s => s === 'active' && refreshUnread());
    return () => {
      cancelled = true;
      cleanup?.();
      sub.remove();
    };
    // Registered again when the role changes, so a buyer made a seller starts getting buyers' part requests.
    // Location: sellers only get buyers' part requests near them.
  }, [signedIn, user?.id, user?.role, lat, lon, countryCode, refreshUnread]);

  useLiveEvent('ChatMessage', (m: ChatMessageEvent) => {
    if (!m?.conversationId) return;
    // The open conversation reads it itself; anything else counts as unread – and has now reached this phone.
    if (activeConversation.current === m.conversationId) return;
    setUnread(n => n + 1);
    ChatApi.delivered(m.conversationId).catch(() => undefined);
  });

  const value = useMemo(() => ({ unread, refreshUnread, activeConversation }), [unread, refreshUnread]);
  return <ChatContext.Provider value={value}>{children}</ChatContext.Provider>;
}

export const useChat = () => useContext(ChatContext);
