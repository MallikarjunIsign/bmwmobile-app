import { fixUrl, http, toServerError } from './api';
import type { ChatListing, ChatMessage, ChatPhoto, Conversation, ConversationSummary, ReportReason } from '../types/chat';
import type { LocalPhoto } from './marketApi';

async function call<T>(request: () => Promise<{ data: T }>, fallback: string): Promise<T> {
  try {
    return (await request()).data;
  } catch (e) {
    throw toServerError(e, fallback);
  }
}

// Photo URLs come from the Gateway's public address; on the emulator localhost is rewritten (see fixUrl).
const fixListing = (l: ChatListing): ChatListing => ({ ...l, imageUrl: fixUrl(l.imageUrl) ?? null });
const fixPhoto = (p: ChatPhoto): ChatPhoto => ({ url: fixUrl(p.url) ?? p.url, thumbnailUrl: fixUrl(p.thumbnailUrl) ?? null });
const fixMessage = (m: ChatMessage): ChatMessage => ({ ...m, photos: (m.photos ?? []).map(fixPhoto) });
const fixConversation = (c: Conversation): Conversation => ({ ...c, listing: fixListing(c.listing), messages: c.messages.map(fixMessage) });

/** Buyer–seller chat (ChatService behind the Gateway at /chat). */
export const ChatApi = {
  inbox: (role: 'all' | 'buying' | 'selling' = 'all') =>
    call<ConversationSummary[]>(() => http.get('/chat/conversations', { params: { role } }), "Couldn't load your messages").then(list =>
      list.map(c => ({ ...c, listing: fixListing(c.listing) }))),

  unread: () => call<{ count: number }>(() => http.get('/chat/unread'), "Couldn't load unread messages").then(r => r.count),

  /** "Chat with seller" (buyer) – or "Message buyer" from the seller's order (pass the buyer). */
  start: (listingId: string, buyer?: string) =>
    call<Conversation>(() => http.post('/chat/conversations', { listingId, buyer }), "Couldn't open the chat").then(fixConversation),

  get: (id: string, before?: string) =>
    call<Conversation>(() => http.get(`/chat/conversations/${id}`, { params: { before } }), "Couldn't load the chat").then(fixConversation),

  // A longer wait than the default 5 s: the first message after the chat server starts can take a few seconds.
  send: (id: string, text: string | undefined, photos: ChatPhoto[] = []) =>
    call<ChatMessage>(() => http.post(`/chat/conversations/${id}/messages`, { text, photos }, { timeout: 20000 }), "Couldn't send the message")
      .then(fixMessage),

  read: (id: string) => call<unknown>(() => http.post(`/chat/conversations/${id}/read`), "Couldn't mark as read"),

  delivered: (id: string) => call<unknown>(() => http.post(`/chat/conversations/${id}/delivered`), "Couldn't confirm delivery"),

  report: (id: string, reason: ReportReason, details?: string) =>
    call<unknown>(() => http.post(`/chat/conversations/${id}/report`, { reason, details }), "Couldn't send the report"),

  block: (username: string) => call<unknown>(() => http.post('/chat/blocks', { username }), "Couldn't block the user"),

  unblock: (username: string) => call<unknown>(() => http.delete(`/chat/blocks/${encodeURIComponent(username)}`), "Couldn't unblock the user"),

  registerDevice: (token: string, platform: 'android' | 'ios') =>
    call<unknown>(() => http.post('/chat/devices', { token, platform }), "Couldn't register for notifications"),

  removeDevice: (token: string) => call<unknown>(() => http.delete(`/chat/devices/${encodeURIComponent(token)}`), "Couldn't unregister"),

  /** Photos for a message (or a review): uploaded and checked like listing photos; max 5. */
  uploadPhotos: (photos: LocalPhoto[], purpose: 'chat' | 'review' = 'chat') => {
    const form = new FormData();
    photos.forEach((p, i) => form.append('files', { uri: p.uri, type: p.type ?? 'image/jpeg', name: p.fileName ?? `photo-${i}.jpg` } as any));
    return call<{ id: string; url: string; thumbnailUrl?: string | null }[]>(
      () =>
        http.post('/media', form, {
          params: { purpose },
          headers: { 'Content-Type': 'multipart/form-data' },
          timeout: 120000,
          transformRequest: data => data,
        }),
      "Couldn't upload the photos",
    );
  },
};
