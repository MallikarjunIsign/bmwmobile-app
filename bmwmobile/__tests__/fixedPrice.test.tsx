import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { AuctionCard } from '../src/components/AuctionCard';
import { commonCategories } from '../src/screens/sell/sellUi';
import { countdown } from '../src/utils/time';
import { endProblem } from '../src/screens/sell/EndTimePicker';
import type { Auction } from '../src/types/models';
import type { Category, PartType } from '../src/types/listing';

jest.mock('../src/location/LocationContext', () => ({
  useLocations: () => ({ status: 'ready', location: null, locationOff: false, country: 'IN', near: undefined }),
}));
jest.mock('../src/auth/session', () => ({
  currentUsername: () => 'alice',
  isCurrentUser: (name?: string | null) => name === 'alice',
}));

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

const listing = (patch: Partial<Auction>): Auction => ({
  id: 'a1',
  title: 'Tyre 265/65 R17 – MRF – New',
  item: { id: 'a1', itemType: 'Part', make: '', model: '', year: 0, description: '', images: [], displayName: '' },
  startingPrice: 9800,
  currentHighBid: 9800,
  bidCount: 0,
  auctionEnd: new Date(Date.now() + (2 * 3600 + 5 * 60 + 9) * 1000 + 500).toISOString(),
  status: 'Live',
  seller: 'ravi123',
  sellerName: 'Ravi Kumar',
  ...patch,
});

test('countdown is hh:mm:ss in hours only (no days)', () => {
  const now = Date.UTC(2026, 9, 12, 10, 0, 0);
  expect(countdown(new Date(now + (5 * 3600 + 42 * 60 + 9) * 1000).toISOString(), now)).toBe('05:42:09');
  expect(countdown(new Date(now + (2 * 86400 + 5 * 3600 + 42 * 60 + 9) * 1000).toISOString(), now)).toBe('53:42:09');
  expect(countdown(new Date(now + 30 * 86400 * 1000).toISOString(), now)).toBe('720:00:00');
  expect(countdown(new Date(now - 1000).toISOString(), now)).toBe('Ended');
});

test('an auction must end 1 hour to 30 days from now', () => {
  const now = Date.now();
  expect(endProblem(new Date(now + 30 * 60000), now)).toMatch(/at least 1 hour/);
  expect(endProblem(new Date(now + 31 * 86400000), now)).toMatch(/at most 30 days/);
  expect(endProblem(new Date(now + 2 * 86400000), now)).toBeNull();
});

test('common parts only see part types that fit any vehicle, and no empty categories', () => {
  const type = (id: number, name: string, fitmentMode: PartType['fitmentMode']) =>
    ({ id, name, fitmentMode, categoryId: 1, category: '', hasPosition: false, hasSide: false, isBodyPart: false, synonyms: [], importantFitmentFields: [], specFields: [] }) as PartType;
  const tree: Category[] = [
    { id: 1, name: 'Body – Exterior', subCategories: [], partTypes: [type(1, 'Bumper', 'VehicleSpecific'), type(2, 'Bonnet', 'VehicleSpecific')] },
    {
      id: 2,
      name: 'Wheels & Tyres',
      subCategories: [{ id: 3, name: 'Hubs', subCategories: [], partTypes: [type(3, 'Wheel hub', 'VehicleSpecific')] }],
      partTypes: [type(4, 'Tyre', 'SizeBased'), type(5, 'Alloy wheel', 'VehicleSpecific')],
    },
  ];

  const common = commonCategories(tree);

  expect(common.map(c => c.name)).toEqual(['Wheels & Tyres']);
  expect(common[0].subCategories).toEqual([]);
  expect(common[0].partTypes.map(t => t.name)).toEqual(['Tyre']);
});

test('auction card: live hh:mm:ss countdown and the seller name, which opens their listings', async () => {
  const onSellerPress = jest.fn();
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AuctionCard auction={listing({ sellerRating: 4.66, sellerRatingCount: 32 })} onPress={jest.fn()} onSellerPress={onSellerPress} />);
  });

  const t = texts(tree!);
  // hh:mm:ss in hours only; the clock keeps ticking while a busy test run renders, so the exact minutes aren't checked.
  expect(t).toMatch(/⏱ 0[12]:\d\d:\d\d/);
  expect(t).toContain('Seller: Ravi Kumar');
  // The seller's rating out of 5 and how many people rated.
  expect(t).toContain('⭐ 4.7/5 (32 ratings)');
  expect(t).toContain('Place Bid');

  const seller = tree!.root.find(n => String(n.props.accessibilityLabel).startsWith('More from Ravi Kumar'));
  await ReactTestRenderer.act(async () => seller.props.onPress());
  expect(onSellerPress).toHaveBeenCalled();
  await ReactTestRenderer.act(async () => tree!.unmount());
});

test('fixed-price card: price, units left and "Buy now – price" – no bids or countdown', async () => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(
      <AuctionCard auction={listing({ saleType: 'FixedPrice', quantityAvailable: 4, auctionEnd: '9999-12-31T00:00:00Z', sellerName: undefined })} onPress={jest.fn()} />,
    );
  });

  const t = texts(tree!);
  expect(t).toContain('BUY NOW');
  expect(t).toContain('Price');
  expect(t).toContain('4 available');
  expect(t).toContain('Buy now – ₹9,800');
  expect(t).not.toContain('bids');
  expect(t).not.toContain('⏱');
  // Older listings without a name show the username.
  expect(t).toContain('Seller: ravi123');
  expect(t).toContain('New seller · no ratings yet');
  await ReactTestRenderer.act(async () => tree!.unmount());
});
