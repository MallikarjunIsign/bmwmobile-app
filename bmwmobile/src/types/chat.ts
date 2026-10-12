/** Buyer–seller chat (ChatService, /chat behind the Gateway). */

export interface ChatListing {
  id: string;
  title: string;
  imageUrl?: string | null;
  /** Current price: the highest bid total, or the price. */
  price: number;
  currency?: string | null;
  /** Live, Sold, Unsold, Cancelled … */
  status: string;
  saleType: 'Auction' | 'FixedPrice';
  deleted: boolean;
}

export type ChatRole = 'buying' | 'selling';

export interface ConversationSummary {
  id: string;
  listing: ChatListing;
  role: ChatRole;
  otherUser: string;
  lastMessage?: string | null;
  lastMessageSender?: string | null;
  lastMessageAt?: string | null;
  unreadCount: number;
  blockedByMe: boolean;
  blockedMe: boolean;
  /** The other person's seller rating ("⭐ 4.7 (32)"). */
  otherUserRating?: number | null;
  otherUserRatingCount?: number;
}

export interface ChatPhoto {
  url: string;
  thumbnailUrl?: string | null;
}

/** sent → delivered → read (the ticks on your own messages). */
export type MessageState = 'sent' | 'delivered' | 'read';

export interface ChatMessage {
  id: string;
  sender: string;
  text?: string | null;
  photos: ChatPhoto[];
  sentAt: string;
  deliveredAt?: string | null;
  readAt?: string | null;
  state: MessageState;
  /** App only: still sending / failed to send. */
  pending?: 'sending' | 'failed';
}

export interface Conversation {
  id: string;
  listing: ChatListing;
  role: ChatRole;
  buyer: string;
  seller: string;
  otherUser: string;
  blockedByMe: boolean;
  blockedMe: boolean;
  /** The listing was removed: the chat is read-only. */
  readOnly: boolean;
  showSafetyTip: boolean;
  /** Oldest first. */
  messages: ChatMessage[];
  hasOlder: boolean;
  otherUserRating?: number | null;
  otherUserRatingCount?: number;
}

export type ReportReason = 'spam' | 'abuse' | 'scam' | 'other';

/** Live from NotificationService. */
export interface ChatMessageEvent {
  conversationId: string;
  messageId: string;
  listingId: string;
  listingTitle?: string | null;
  sender: string;
  recipient: string;
  preview: string;
  sentAt: string;
}

export interface ChatReceiptEvent {
  conversationId: string;
  notifyUser: string;
  state: 'Delivered' | 'Read';
  upTo: string;
}
