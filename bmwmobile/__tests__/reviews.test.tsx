import React from 'react';
import { Alert } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { WriteReviewScreen } from '../src/screens/WriteReviewScreen';
import { SellerProfileScreen } from '../src/screens/SellerProfileScreen';
import { OrderTrackingCard } from '../src/components/OrderTrackingCard';
import { ReviewCard } from '../src/components/ReviewCard';
import { ReviewApi } from '../src/services/reviewApi';
import { MarketApi } from '../src/services/marketApi';
import { ratingText } from '../src/utils/rating';
import type { Review, SellerRatingSummary } from '../src/types/review';
import type { Order } from '../src/types/listing';

jest.setTimeout(20000);

jest.mock('../src/services/reviewApi', () => ({
  ReviewApi: {
    create: jest.fn(), update: jest.fn(), get: jest.fn(), reply: jest.fn(), report: jest.fn(),
    seller: jest.fn(), sellerReviews: jest.fn(), publicProfile: jest.fn(), forListing: jest.fn(),
  },
}));
jest.mock('../src/services/marketApi', () => ({
  MarketApi: { search: jest.fn(), hitToAuction: (h: any) => h, orderStep: jest.fn(), chooseDelivery: jest.fn(), shippingAddresses: jest.fn() },
}));
jest.mock('../src/services/chatApi', () => ({ ChatApi: { uploadPhotos: jest.fn() } }));
jest.mock('react-native-image-picker', () => ({ launchImageLibrary: jest.fn(), launchCamera: jest.fn() }));
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => ({ location: null, country: 'IN', locationOff: false }) }));
jest.mock('../src/auth/session', () => ({
  currentUsername: () => 'alice',
  isCurrentUser: (name?: string | null) => name === 'alice',
}));

const api = ReviewApi as unknown as Record<string, jest.Mock>;
const market = MarketApi as unknown as Record<string, jest.Mock>;

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, match: (t: string) => boolean) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && match(textOf(n)));
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};
const pressLabel = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  const node = tree.root.find(n => n.props.accessibilityLabel === label && n.props.onPress);
  await ReactTestRenderer.act(async () => node.props.onPress());
};

const navigation = { goBack: jest.fn(), navigate: jest.fn() } as any;
const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const mount = async (element: React.ReactElement) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(element);
  });
  mounted.push(tree!);
  return tree!;
};

afterEach(async () => {
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
  jest.restoreAllMocks();
});
beforeEach(() => {
  Object.values(api).forEach(m => m.mockReset());
  Object.values(market).forEach(m => typeof m.mockReset === 'function' && m.mockReset());
  navigation.navigate.mockReset();
});

const review: Review = {
  id: 'r1', orderId: 'o1', listingId: 'a1', itemTitle: 'Dash Cam – 70mai – New', buyer: 'c***l', seller: 'bob', itemRating: 5, sellerRating: 4,
  communication: 5, accuracy: null, delivery: 3, tags: ['Fast replies', 'Slow to respond'], comment: 'Exactly as shown.', photos: [],
  status: 'Published', createdAt: '2026-10-11T10:00:00Z', updatedAt: '2026-10-11T10:00:00Z', canEdit: false, canReply: true, isMine: false,
};

test('rating text: out of 5 with how many rated; "New seller" added below 3 ratings', () => {
  expect(ratingText(4.66, 32)).toBe('⭐ 4.7/5 (32 ratings)');
  expect(ratingText(4, 1)).toBe('⭐ 4.0/5 (1 rating) · New seller');
  expect(ratingText(null, 0)).toBe('New seller · no ratings yet');
  // Two-column cards.
  expect(ratingText(4.66, 32, true)).toBe('⭐ 4.7/5 (32)');
  expect(ratingText(null, 0, true)).toBe('No ratings yet');
});

test('write a review: both star ratings are required; quick ratings, tags and comment are sent', async () => {
  api.create.mockResolvedValue({ ...review, isMine: true, canEdit: true, buyer: 'alice' });
  jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
  const tree = await mount(
    <WriteReviewScreen navigation={navigation}
      route={{ key: 'w', name: 'WriteReview', params: { orderId: 'o1', title: 'Dash Cam – 70mai – New', seller: 'bob' } } as any} />,
  );

  let t = texts(tree);
  expect(t).toContain('Did it match the description?');
  expect(t).toContain('Sold by bob');
  REVIEW_TAG_LABELS.forEach(tag => expect(t).toContain(tag));

  await press(tree, x => x === 'Post review');
  t = texts(tree);
  expect(t).toContain('Rate the item');
  expect(t).toContain('Rate the seller');
  expect(api.create).not.toHaveBeenCalled();

  await pressLabel(tree, 'The item: 5 stars');
  await pressLabel(tree, 'The seller: 4 stars');
  await pressLabel(tree, 'Communication: 5 stars');
  await press(tree, x => x === 'Fast replies');
  const input = tree.root.find(n => (n.type as unknown) === 'TextInput');
  await ReactTestRenderer.act(async () => input.props.onChangeText('  Exactly as shown.  '));
  await press(tree, x => x === 'Post review');

  expect(api.create).toHaveBeenCalledWith({
    orderId: 'o1', itemRating: 5, sellerRating: 4, communication: 5, accuracy: null, delivery: null,
    tags: ['Fast replies'], comment: 'Exactly as shown.', photoIds: [],
  });
  // After posting: the review is shown, with "Edit" for 7 days.
  expect(texts(tree)).toContain('✎ Edit your review');
});

const REVIEW_TAG_LABELS = ['Fast replies', 'Well packed', 'As described', 'On time', 'Not as described', 'Slow to respond'];

test('an existing review opens read-only; after 7 days it can no longer be edited', async () => {
  api.get.mockResolvedValue({ ...review, isMine: true, buyer: 'alice', canEdit: false, canReply: false });
  const tree = await mount(
    <WriteReviewScreen navigation={navigation}
      route={{ key: 'w', name: 'WriteReview', params: { orderId: 'o1', reviewId: 'r1', title: 'Dash Cam', seller: 'bob' } } as any} />,
  );

  const t = texts(tree);
  expect(t).toContain('Your review');
  expect(t).toContain('Exactly as shown.');
  expect(t).toContain('Reviews can be edited within 7 days of posting.');
  expect(t).not.toContain('Post review');
});

test('review card: verified purchase, quick ratings and tags; the seller replies once; others can report', async () => {
  api.reply.mockResolvedValue({ ...review, sellerReply: 'Thanks for buying!', canReply: false });
  const onChange = jest.fn();
  const tree = await mount(<ReviewCard review={review} onChange={onChange} showItem />);

  const t = texts(tree);
  expect(t).toContain('✓ Verified purchase');
  expect(t).toContain('Communication 5★ · Delivery 3★');
  expect(t).toContain('c***l');
  expect(t).toContain('Dash Cam – 70mai – New');

  await press(tree, x => x === '↩ Reply');
  const input = tree.root.find(n => (n.type as unknown) === 'TextInput');
  await ReactTestRenderer.act(async () => input.props.onChangeText('Thanks for buying!'));
  await press(tree, x => x === 'Post reply');
  expect(api.reply).toHaveBeenCalledWith('r1', 'Thanks for buying!');
  expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ sellerReply: 'Thanks for buying!' }));

  api.report.mockResolvedValue(undefined);
  const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
  await press(tree, x => x === 'Report');
  const reasons = alert.mock.calls.at(-1)![2]!;
  expect(reasons.map(r => r.text)).toEqual(['Spam', 'Abusive', 'Fake review', 'Personal details', 'Other', 'Cancel']);
  await ReactTestRenderer.act(async () => reasons[2].onPress!());
  expect(api.report).toHaveBeenCalledWith('r1', 'fake');
});

test('seller profile: who, average and count, 5★…1★ breakdown, quick-rating averages, sales, reviews and live listings', async () => {
  const summary: SellerRatingSummary = {
    seller: 'bob', average: 4.7, count: 32, isNewSeller: false, breakdown: [25, 5, 1, 1, 0], communicationAverage: 4.8,
    accuracyAverage: 4.6, deliveryAverage: null, itemAverage: 4.7, completedSales: 40, latestReviews: [review],
  };
  api.seller.mockResolvedValue(summary);
  api.publicProfile.mockResolvedValue({ username: 'bob', displayName: 'Bob Kumar', memberSince: '2025-03-04T00:00:00Z', city: 'Hyderabad', countryCode: 'IN' });
  market.search.mockResolvedValue({ results: [] });
  const tree = await mount(<SellerProfileScreen navigation={navigation} route={{ key: 's', name: 'SellerProfile', params: { seller: 'bob' } } as any} />);

  const t = texts(tree);
  expect(t).toContain('Bob Kumar');
  expect(t).toContain('Member since Mar 2025 · Hyderabad, India');
  expect(t).toContain('4.7');
  expect(t).toContain('32 ratings');
  expect(t).not.toContain('New seller');
  expect(t).toContain('4.8★');
  expect(t).toContain('40');
  expect(t).toContain('Exactly as shown.');
  expect(t).toContain('Show more reviews');
  // A buyer sees the seller's listings they could get (here no location: the buyer's country).
  expect(market.search).toHaveBeenCalledWith({ seller: 'bob', sort: 'newest', pageSize: 50 }, undefined, 'IN');
});

test('order card: the buyer is asked to rate right after completing; then "Rate your purchase" opens the review', async () => {
  const order = {
    id: 'o1', auctionId: 'a1', role: 'buyer', seller: 'bob', buyer: 'alice', otherParty: 'bob', title: 'Dash Cam', finalTotal: 1200,
    deliveryOptions: ['Pickup'], deliveryMethod: 'Pickup', deliveryChoices: [], status: 'PickedUp', statusLabel: 'Picked up',
    tracking: [], actions: [{ status: 'Completed', label: 'Completed' }], createdAt: '2026-10-11T09:00:00Z',
  } as unknown as Order;
  const completed = { ...order, status: 'Completed', actions: [], canReview: true } as Order;
  market.orderStep.mockResolvedValue(completed);
  const alert = jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => buttons?.[1]?.onPress?.());
  const onReview = jest.fn();
  const onChange = jest.fn();
  const tree = await mount(
    <OrderTrackingCard order={order} money={n => `₹${n}`} onChange={onChange} onOpenListing={jest.fn()} onReview={onReview} />,
  );

  await press(tree, x => x === 'Confirm you picked it up');
  expect(alert).toHaveBeenCalledWith('How was your purchase?', expect.any(String), expect.any(Array));
  expect(onReview).toHaveBeenCalledWith(completed);

  const done = await mount(
    <OrderTrackingCard order={completed} money={n => `₹${n}`} onChange={onChange} onOpenListing={jest.fn()} onReview={onReview} />,
  );
  await press(done, x => x === '⭐ Rate your purchase');
  expect(onReview).toHaveBeenLastCalledWith(completed);
});
