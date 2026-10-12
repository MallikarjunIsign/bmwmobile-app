/** Ratings & reviews (AuctionService /reviews, /sellers; IdentityService public profile). */

export const REVIEW_TAGS = ['Fast replies', 'Well packed', 'As described', 'On time', 'Not as described', 'Slow to respond'] as const;
export type ReviewTag = (typeof REVIEW_TAGS)[number];

export type ReviewReportReason = 'spam' | 'abuse' | 'fake' | 'personal' | 'other';

export interface ReviewPhoto {
  id: string;
  url: string;
  thumbnailUrl?: string | null;
}

export interface Review {
  id: string;
  orderId: string;
  listingId: string;
  itemTitle: string;
  itemImageUrl?: string | null;
  /** Partly hidden ("a***e") unless it's yours. */
  buyer: string;
  seller: string;
  itemRating: number;
  sellerRating: number;
  communication?: number | null;
  accuracy?: number | null;
  delivery?: number | null;
  tags: string[];
  comment?: string | null;
  photos: ReviewPhoto[];
  sellerReply?: string | null;
  sellerReplyAt?: string | null;
  status: 'Published' | 'Hidden';
  createdAt: string;
  updatedAt: string;
  canEdit: boolean;
  canReply: boolean;
  isMine: boolean;
}

/** POST /reviews (with orderId) or PUT /reviews/:id. */
export interface SaveReview {
  orderId?: string;
  itemRating: number;
  sellerRating: number;
  communication?: number | null;
  accuracy?: number | null;
  delivery?: number | null;
  tags: string[];
  comment?: string;
  photoIds: string[];
}

export interface SellerRatingSummary {
  seller: string;
  average?: number | null;
  count: number;
  /** Fewer than 3 ratings. */
  isNewSeller: boolean;
  /** 5★, 4★, 3★, 2★, 1★ counts. */
  breakdown: number[];
  communicationAverage?: number | null;
  accuracyAverage?: number | null;
  deliveryAverage?: number | null;
  itemAverage?: number | null;
  completedSales: number;
  latestReviews: Review[];
}

/** IdentityService: what anyone may see about a user. */
export interface PublicProfile {
  username: string;
  displayName?: string | null;
  memberSince?: string | null;
  city?: string | null;
  countryCode?: string | null;
}
