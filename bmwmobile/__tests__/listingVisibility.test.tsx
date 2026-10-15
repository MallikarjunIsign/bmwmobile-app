import React from 'react';
import { Alert } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { AuctionCard } from '../src/components/AuctionCard';
import { buyerDeliveryText, receiveProblem } from '../src/utils/delivery';
import { formatMoney } from '../src/utils/geo';
import type { Auction } from '../src/types/models';
import type { ShippingQuote } from '../src/types/listing';

// A buyer in Houston (USA first): miles and dollars.
const mockLocations = { country: 'US', location: { latitude: 29.7604, longitude: -95.3698, radiusKm: 321.87 } };
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => mockLocations }));
jest.mock('../src/auth/session', () => ({ isCurrentUser: () => false }));

const money = (n: number) => formatMoney(n, 'US');
const quote = (patch: Partial<ShippingQuote> = {}): ShippingQuote => ({
  available: true, charge: 12, distance: 940, unit: 'mi', currency: 'USD', sizeClass: 'Medium', reason: null, placeholderRates: false, ...patch,
} as ShippingQuote);

const KM = 1.609344;

describe('what the buyer can do, on cards and the listing', () => {
  test('inside the radius: pickup with the distance, and shipping with the charge', () => {
    expect(buyerDeliveryText({ deliveryOptions: ['Pickup'], distanceKm: 12 * KM, withinRadius: true }, money, 'US'))
      .toBe('📍 Pickup · 12 mi away');
    expect(buyerDeliveryText({ deliveryOptions: ['Pickup', 'Shipping'], distanceKm: 12 * KM, withinRadius: true, shippingQuote: quote() }, money, 'US'))
      .toBe('📍 Pickup · 12 mi away · 🚚 Shipping ~$12');
    // India: km and rupees.
    expect(buyerDeliveryText({ deliveryOptions: ['Pickup', 'Shipping'], distanceKm: 12, withinRadius: true, shippingQuote: quote({ charge: 350 }) },
      n => formatMoney(n, 'IN'), 'IN')).toBe('📍 Pickup · 12 km away · 🚚 Shipping ~₹350');
  });

  test('outside the radius, it ships: "Ships to you" with the charge and distance – no pickup badge', () => {
    const text = buyerDeliveryText(
      { deliveryOptions: ['Pickup', 'Shipping'], distanceKm: 640 * KM, withinRadius: false, shippingQuote: quote({ charge: 1200 }) }, money, 'US');
    expect(text).toBe('🚚 Ships to you ~$1,200 · 640 mi away');
    expect(text).not.toContain('Pickup');
    // While the charge is loading.
    expect(buyerDeliveryText({ deliveryOptions: ['Shipping'], distanceKm: 640 * KM, withinRadius: false }, money, 'US'))
      .toBe('🚚 Ships to you · 640 mi away');
  });

  test('shipping can\'t be worked out for this buyer: the listing says so', () => {
    const notAvailable = quote({ available: false, charge: null, reason: 'Too far to ship' });
    expect(buyerDeliveryText({ deliveryOptions: ['Shipping'], distanceKm: 640 * KM, withinRadius: false, shippingQuote: notAvailable }, money, 'US'))
      .toBe('🚚 Shipping not available to your location · 640 mi away');
  });

  test('without a location: the listing\'s own summary, as before', () => {
    expect(buyerDeliveryText({ deliveryOptions: ['Pickup', 'Shipping'], pickupCity: 'Houston' }, money, 'US'))
      .toBe('📍 Pickup in Houston (free) · 🚚 Shipping');
  });

  test('Buy now / Place bid: why, and whether there is any way to get it', () => {
    const notAvailable = quote({ available: false, charge: null, reason: 'Too far to ship' });
    expect(receiveProblem({ deliveryOptions: ['Shipping'], withinRadius: false, shippingQuote: notAvailable }, 'US'))
      .toEqual({ blocking: true, message: expect.stringContaining("you can't receive this item") });
    expect(receiveProblem({ deliveryOptions: ['Pickup', 'Shipping'], distanceKm: 640 * KM, withinRadius: false, shippingQuote: notAvailable }, 'US'))
      .toEqual({ blocking: false, message: expect.stringContaining('pick it up yourself. It is 640 mi away.') });
    expect(receiveProblem({ deliveryOptions: ['Shipping'], withinRadius: false, shippingQuote: quote() }, 'US')).toBeNull();
  });
});

const listing = (patch: Partial<Auction>): Auction => ({
  id: 'a1', title: 'Remanufactured alternator', startingPrice: 165, currentHighBid: 165, bidCount: 0, seller: 'bob',
  auctionEnd: new Date(Date.now() + 86400000).toISOString(), status: 'Live', saleType: 'Auction',
  item: { id: 'a1', itemType: 'Part', make: '', model: '', year: 0, description: '', images: [], displayName: '' },
  deliveryOptions: ['Pickup', 'Shipping'], locationName: 'Long Beach, CA', ...patch,
} as Auction);

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
afterEach(async () => {
  // The cards' countdowns keep ticking otherwise.
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
});

const render = async (auction: Auction, onBidPress = jest.fn()) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AuctionCard auction={auction} onPress={jest.fn()} onBidPress={onBidPress} />);
  });
  mounted.push(tree!);
  return tree!;
};
const delivery = (tree: ReactTestRenderer.ReactTestRenderer) => textOf(tree.root.find(n => n.props.testID === 'card-delivery'));

describe('card badges', () => {
  test('inside the radius: "📍 Pickup · 12 mi away" and the shipping charge', async () => {
    const tree = await render(listing({ distanceKm: 12 * KM, withinRadius: true, shippingQuote: quote() }));
    expect(delivery(tree)).toBe('📍 Pickup · 12 mi away · 🚚 Shipping ~$12');
  });

  test('outside the radius: "🚚 Ships to you ~$1,200 · 640 mi away", no pickup', async () => {
    const tree = await render(listing({ distanceKm: 640 * KM, withinRadius: false, shippingQuote: quote({ charge: 1200 }) }));
    expect(delivery(tree)).toBe('🚚 Ships to you ~$1,200 · 640 mi away');
  });

  test('Place bid when shipping isn\'t available and pickup isn\'t offered: explains why and doesn\'t open the bid', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    const onBid = jest.fn();
    const tree = await render(listing({
      deliveryOptions: ['Shipping'], distanceKm: 640 * KM, withinRadius: false,
      shippingQuote: quote({ available: false, charge: null, reason: 'Too far to ship' }),
    }), onBid);
    expect(delivery(tree)).toContain('Shipping not available to your location');

    let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && textOf(n) === 'Place Bid');
    while (node && !node.props.onPress) node = node.parent;
    await ReactTestRenderer.act(async () => node!.props.onPress());

    expect(alert).toHaveBeenCalledWith('Shipping not available to your location', expect.stringContaining('Too far to ship'));
    expect(onBid).not.toHaveBeenCalled();
    alert.mockRestore();
  });
});
