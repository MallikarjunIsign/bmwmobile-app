/** Fewer ratings than this: also marked "New seller". */
export const NEW_SELLER_BELOW = 3;

/**
 * The seller's rating out of 5 and how many people rated: "⭐ 4.7/5 (32 ratings)", "⭐ 4.0/5 (1 rating) · New seller",
 * or "New seller · no ratings yet". `short` (two-column cards): "⭐ 4.7/5 (32)".
 */
export function ratingText(average?: number | null, count = 0, short = false): string {
  if (!average || count <= 0) return short ? 'No ratings yet' : 'New seller · no ratings yet';
  const score = `⭐ ${average.toFixed(1)}/5`;
  if (short) return `${score} (${count})`;
  return `${score} (${count} ${count === 1 ? 'rating' : 'ratings'})${count < NEW_SELLER_BELOW ? ' · New seller' : ''}`;
}

/** "Member since Oct 2026". */
export function memberSince(iso?: string | null): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return `Member since ${months[d.getMonth()]} ${d.getFullYear()}`;
}
