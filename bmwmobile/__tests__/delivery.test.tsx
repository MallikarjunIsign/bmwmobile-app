import { deliverySummary, shipsWithinText } from '../src/utils/delivery';
import { totalBreakdown } from '../src/components/OrderTrackingCard';
import type { Order } from '../src/types/listing';

const money = (n: number) => `₹${n.toLocaleString('en-IN')}`;

test('cards and the listing show what the buyer can get', () => {
  expect(deliverySummary({ deliveryOptions: ['Pickup', 'Shipping'], pickupCity: 'Hyderabad', shippingFee: 500 }, money))
    .toBe('📍 Pickup in Hyderabad · 🚚 Shipping ₹500');
  expect(deliverySummary({ deliveryOptions: ['Pickup', 'Shipping'], pickupCity: 'Hyderabad', shippingFee: 0 }, money))
    .toBe('📍 Pickup in Hyderabad · 🚚 Free shipping');
  expect(deliverySummary({ deliveryOptions: ['Pickup'], pickupCity: 'Hyderabad' }, money)).toBe('📍 Pickup only · Hyderabad');
  expect(deliverySummary({ deliveryOptions: ['Shipping'], shippingFee: 500 }, money)).toBe('🚚 Shipping only · ₹500');
  // Older listings: no pickup city yet (the location name is used), shipping fee not set.
  expect(deliverySummary({ deliveryOptions: ['Pickup', 'Shipping'], locationName: 'Kukatpally, Hyderabad' }, money))
    .toBe('📍 Pickup in Hyderabad · 🚚 Shipping');
  expect(deliverySummary({ deliveryOptions: [] }, money)).toBeNull();
  expect(shipsWithinText('IN')).toBe('Ships within India');
});

test('order total: part price + winning bid + shipping fee (only when shipped)', () => {
  const order = { finalTotal: 14100, partPrice: 14000, winningBid: 100, shippingFee: 500, total: 14600, deliveryMethod: 'Shipping' } as Order;
  expect(totalBreakdown(order, money)).toBe('Part price ₹14,000 + winning bid ₹100 + shipping ₹500 = ₹14,600');
  expect(totalBreakdown({ ...order, deliveryMethod: 'Pickup', total: 14100 }, money)).toBe('Part price ₹14,000 + winning bid ₹100 = ₹14,100');
  expect(totalBreakdown({ ...order, shippingFee: 0 }, money)).toBe('Part price ₹14,000 + winning bid ₹100 + free shipping = ₹14,600');
  // Direct sale of 2 units, picked up.
  expect(totalBreakdown({ finalTotal: 1000, partPrice: 1000, winningBid: 0, saleType: 'FixedPrice', quantity: 2, deliveryMethod: 'Pickup', total: 1000 } as Order, money))
    .toBe('2 × ₹500');
});
