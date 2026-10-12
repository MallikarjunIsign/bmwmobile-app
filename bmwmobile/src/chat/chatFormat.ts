import { REGIONS, formatMoney } from '../utils/geo';
import type { CountryCode } from '../types/location';
import type { MessageState } from '../types/chat';
import type { GarageVehicle } from '../types/listing';

/** A listing's price in its own currency (₹ / $ / ₦ / €), whatever country the viewer is in. */
export function moneyIn(amount: number, currency?: string | null): string {
  const country = (Object.keys(REGIONS) as CountryCode[]).find(c => REGIONS[c].currencyCode === currency);
  return formatMoney(amount, country);
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

const clock = (d: Date) => {
  const h = d.getHours();
  return `${h % 12 || 12}:${String(d.getMinutes()).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`;
};

/** Inbox times: "3:42 pm" today, "Yesterday", "Mon" this week, else "12 Oct". */
export function inboxTime(iso?: string | null, now = new Date()): string {
  if (!iso) return '';
  const d = new Date(iso);
  const days = Math.floor((new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime() - new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()) / 86400000);
  if (days <= 0) return clock(d);
  if (days === 1) return 'Yesterday';
  if (days < 7) return DAYS[d.getDay()];
  return `${d.getDate()} ${MONTHS[d.getMonth()]}`;
}

/** Message times: "3:42 pm", with the date when it isn't today ("12 Oct, 3:42 pm"). */
export function messageTime(iso: string, now = new Date()): string {
  const d = new Date(iso);
  const today = d.toDateString() === now.toDateString();
  return today ? clock(d) : `${d.getDate()} ${MONTHS[d.getMonth()]}, ${clock(d)}`;
}

/** ✓ sent, ✓✓ delivered, ✓✓ (highlighted) read. */
export const ticks = (state: MessageState) => (state === 'sent' ? '✓' : '✓✓');

/** The first-message chips for a buyer. */
export function quickQuestions(garage: GarageVehicle | null, city: string | null | undefined): string[] {
  return [
    'Is this still available?',
    garage ? `Does it fit my ${garage.year} ${garage.make} ${garage.model}?` : 'Does it fit my vehicle?',
    'Can you send more photos?',
    city ? `Is pickup / shipping possible to ${city}?` : 'Is pickup / shipping possible to my city?',
  ];
}

export const SAFETY_TIP = 'Keep payments and personal details inside the app until the item is sold.';
