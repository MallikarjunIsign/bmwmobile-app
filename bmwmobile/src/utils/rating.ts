/** Fewer ratings than this: "New seller". */
export const NEW_SELLER_BELOW = 3;

/** "⭐ 4.7 (32)", or "New seller" with fewer than 3 ratings. */
export function ratingText(average?: number | null, count = 0): string {
  if (!average || count < NEW_SELLER_BELOW) return 'New seller';
  return `⭐ ${average.toFixed(1)} (${count})`;
}

/** "Member since Oct 2026". */
export function memberSince(iso?: string | null): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return `Member since ${months[d.getMonth()]} ${d.getFullYear()}`;
}
