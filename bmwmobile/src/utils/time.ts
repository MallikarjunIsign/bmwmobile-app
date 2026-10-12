/** "2d 4h", "3h 12m", "8m 05s", "Ended" – time left until an ISO date. */
export function timeLeft(endIso: string, now = Date.now()): string {
  const ms = new Date(endIso).getTime() - now;
  if (!(ms > 0)) return 'Ended';
  const s = Math.floor(ms / 1000);
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m ${String(s % 60).padStart(2, '0')}s`;
}

/**
 * Live countdown until an ISO date in hours only: "05:42:09", "53:42:09" (no days), "Ended" afterwards.
 */
export function countdown(endIso: string, now = Date.now()): string {
  const ms = new Date(endIso).getTime() - now;
  if (!(ms > 0)) return 'Ended';
  const s = Math.floor(ms / 1000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(Math.floor(s / 3600))}:${pad(Math.floor((s % 3600) / 60))}:${pad(s % 60)}`;
}

/** Bidder names in the bid history are partly hidden: "alice" → "a***e". */
export function maskUsername(name: string): string {
  if (name.length <= 2) return `${name[0] ?? ''}*`;
  return `${name[0]}${'*'.repeat(Math.min(3, name.length - 2))}${name[name.length - 1]}`;
}

/** "GenuineOem" → "Genuine OEM", "VehicleSpecificPart" → "Vehicle-specific part", "EndedNoBids" → "Ended – no bids". */
export function label(value?: string | null): string {
  if (!value) return '';
  const known: Record<string, string> = {
    GenuineOem: 'Genuine OEM',
    OemEquivalent: 'OEM-equivalent',
    Aftermarket: 'Aftermarket',
    ForRepair: 'For repair',
    CommonPart: 'Common part',
    VehicleSpecificPart: 'Vehicle-specific part',
    Vehicle: 'Vehicle',
    VehicleSpecific: 'Vehicle-specific',
    SizeBased: 'Fits by size',
    Universal: 'Universal',
    EndedNoBids: 'Ended – no bids',
    SoldAwaitingHandover: 'Sold',
    ReadyForPickup: 'Ready for pickup',
    PickedUp: 'Picked up',
    RunsAndDrives: 'Runs & drives',
    RunsNotDrivable: 'Runs, not drivable',
    DoesNotRun: 'Does not run',
    EngineBay: 'Engine bay',
    WheelsAndTyres: 'Wheels & Tyres',
  };
  return known[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2');
}

/** "11 Oct, 3:42 pm" – when an order step happened. */
export function formatWhen(iso?: string | null): string | undefined {
  if (!iso) return undefined;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return undefined;
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const h = d.getHours();
  return `${d.getDate()} ${months[d.getMonth()]}, ${h % 12 || 12}:${String(d.getMinutes()).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`;
}
