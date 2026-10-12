import axios from 'axios';
import { config } from '../config';
import { fixUrl, http, toServerError } from './api';
import type { PublicProfile, Review, ReviewReportReason, SaveReview, SellerRatingSummary } from '../types/review';

async function call<T>(request: () => Promise<{ data: T }>, fallback: string): Promise<T> {
  try {
    return (await request()).data;
  } catch (e) {
    throw toServerError(e, fallback);
  }
}

const fixReview = (r: Review): Review => ({
  ...r,
  itemImageUrl: fixUrl(r.itemImageUrl) ?? null,
  photos: (r.photos ?? []).map(p => ({ ...p, url: fixUrl(p.url) ?? p.url, thumbnailUrl: fixUrl(p.thumbnailUrl) ?? null })),
});

// Public profiles come straight from IdentityService (no sign-in needed).
const identity = axios.create({ baseURL: config.IDENTITY_URL, timeout: 8000 });

/** Ratings & reviews. */
export const ReviewApi = {
  create: (body: SaveReview) => call<Review>(() => http.post('/reviews', body), "Couldn't post your review").then(fixReview),

  update: (id: string, body: SaveReview) => call<Review>(() => http.put(`/reviews/${id}`, body), "Couldn't save your review").then(fixReview),

  get: (id: string) => call<Review>(() => http.get(`/reviews/${id}`), "Couldn't load the review").then(fixReview),

  /** The seller's one public reply. */
  reply: (id: string, text: string) =>
    call<Review>(() => http.post(`/reviews/${id}/reply`, { text }), "Couldn't post your reply").then(fixReview),

  report: (id: string, reason: ReviewReportReason, details?: string) =>
    call<unknown>(() => http.post(`/reviews/${id}/report`, { reason, details }), "Couldn't send the report"),

  /** Item reviews of a listing (sold items). */
  forListing: (listingId: string) =>
    call<Review[]>(() => http.get(`/reviews/listing/${listingId}`), "Couldn't load the reviews").then(list => list.map(fixReview)),

  seller: (username: string) =>
    call<SellerRatingSummary>(() => http.get(`/sellers/${encodeURIComponent(username)}`), "Couldn't load the seller's ratings").then(s => ({
      ...s,
      latestReviews: s.latestReviews.map(fixReview),
    })),

  sellerReviews: (username: string, page: number) =>
    call<Review[]>(() => http.get(`/sellers/${encodeURIComponent(username)}/reviews`, { params: { page } }), "Couldn't load the reviews")
      .then(list => list.map(fixReview)),

  publicProfile: (username: string) =>
    call<PublicProfile>(() => identity.get(`/api/users/${encodeURIComponent(username)}/public`), "Couldn't load the profile"),
};
