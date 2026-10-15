import { deliverySummary, orderShippingText, shippingChargeText, shippingEstimateText, shipsWithinText } from '../src/utils/delivery';
import { totalBreakdown } from '../src/components/OrderTrackingCard';
import type { Order, ShippingQuote } from '../src/types/listing';

const money = (n: number) => `₹${n.toLocaleString('en-IN')}`;

const quote = (patch: Partial<ShippingQuote> = {}): ShippingQuote => ({
  available: true, charge: 350, distance: 22, unit: 'km', currency: 'INR', sizeClass: 'Medium', reason: null, placeholderRates: true, ...patch,
});

test('cards and the listing show what the buyer can get – pickup is free, shipping is calculated', () => {
  expect(deliverySummary({ deliveryOptions: ['Pickup', 'Shipping'], pickupCity: 'Hyderabad' })).toBe('📍 Pickup in Hyderabad (free) · 🚚 Shipping');
  expect(deliverySummary({ deliveryOptions: ['Pickup'], pickupCity: 'Hyderabad' })).toBe('📍 Pickup only · Hyderabad');
  expect(deliverySummary({ deliveryOptions: ['Shipping'] })).toBe('🚚 Shipping only');
  // Older listings: no pickup city yet (the location name is used).
  expect(deliverySummary({ deliveryOptions: ['Pickup', 'Shipping'], locationName: 'Kukatpally, Hyderabad' })).toBe('📍 Pickup in Hyderabad (free) · 🚚 Shipping');
  expect(deliverySummary({ deliveryOptions: [] })).toBeNull();
  expect(shipsWithinText('IN')).toBe('Ships within India');
});

test('listing: "Shipping to Kukatpally: ~₹350 (estimate)", or why it is not available', () => {
  expect(shippingEstimateText(quote(), 'Kukatpally', money)).toBe('Shipping to Kukatpally: ~₹350 (estimate)');
  expect(shippingEstimateText(quote({ available: false, charge: null, reason: 'Ships within India only' }), 'Lagos', money))
    .toBe('Shipping not available to Lagos: Ships within India only');
  expect(shippingChargeText(quote(), money)).toBe('₹350 · about 22 km');
  expect(shippingChargeText(quote({ unit: 'mi', distance: 104, charge: 46 }), n => `$${n}`)).toBe('$46 · about 104 mi');
});

test('order breakdown: part price + winning bid + the fixed shipping charge (or pickup free) = total', () => {
  const order = {
    finalTotal: 14100, partPrice: 14000, winningBid: 100, shippingCharge: 350, shippingDistance: 22, shippingDistanceUnit: 'km',
    total: 14450, deliveryMethod: 'Shipping',
  } as Order;
  expect(totalBreakdown(order, money)).toBe('Part price ₹14,000 + winning bid ₹100 + shipping ₹350 (22 km) = ₹14,450');
  expect(totalBreakdown({ ...order, deliveryMethod: 'Pickup', shippingCharge: 0, total: 14100 }, money))
    .toBe('Part price ₹14,000 + winning bid ₹100 + pickup free = ₹14,100');
  // Not chosen yet: nothing about delivery.
  expect(totalBreakdown({ ...order, deliveryMethod: null, shippingCharge: null, total: 14100 }, money))
    .toBe('Part price ₹14,000 + winning bid ₹100 = ₹14,100');
  // Older orders: the seller's fee.
  expect(orderShippingText({ deliveryMethod: 'Shipping', shippingFee: 500 } as Order, money)).toBe('shipping ₹500');
  // Direct sale of 2 units.
  expect(totalBreakdown({ finalTotal: 1000, partPrice: 1000, winningBid: 0, saleType: 'FixedPrice', quantity: 2, deliveryMethod: null, total: 1000 } as Order, money))
    .toBe('2 × ₹500');
});

test('US: USPS Ground Advantage with its zone and billable weight; freight when too big', () => {
  const usd = (n: number) => `$${n}`;
  const usps = quote({ charge: 12, unit: 'mi', currency: 'USD', service: 'USPS Ground Advantage', zone: 2, billableWeightLb: 2, distance: 81 });
  expect(shippingEstimateText(usps, 'Philadelphia', usd)).toBe('Shipping to Philadelphia: ~$12 (USPS Ground Advantage, estimate)');
  expect(shippingChargeText(usps, usd)).toBe('$12 · USPS Ground Advantage, zone 2 · 2 lb');
  // Freight / vehicles: US industry averages until a carrier is chosen – labelled "estimated rate" everywhere.
  const freight = quote({ charge: 287, unit: 'mi', service: 'Freight', distance: 105, estimatedRate: true });
  expect(shippingChargeText(freight, usd)).toBe('$287 · Freight, estimated rate · about 105 mi');
  expect(shippingEstimateText(freight, 'Philadelphia', usd)).toBe('Shipping to Philadelphia: ~$287 (Freight, estimated rate)');
  expect(orderShippingText({ deliveryMethod: 'Shipping', shippingCharge: 287, shippingService: 'Freight', shippingRateEstimated: true } as Order, usd))
    .toBe('shipping $287 (Freight, estimated rate)');
  // Real carrier rates: no label.
  expect(shippingChargeText({ ...freight, estimatedRate: false }, usd)).toBe('$287 · Freight · about 105 mi');

  const order = { finalTotal: 120, partPrice: 120, winningBid: 0, shippingCharge: 12, shippingService: 'USPS Ground Advantage', shippingZone: 2,
    total: 132, deliveryMethod: 'Shipping' } as Order;
  expect(totalBreakdown(order, usd)).toBe('Part price $120 + shipping $12 (USPS Ground Advantage, zone 2) = $132');
});

