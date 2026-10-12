import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { PlaceBidModal } from '../src/screens/PlaceBidModal';
import { ApiService, ServerError } from '../src/services/api';

jest.setTimeout(20000);

jest.mock('../src/location/LocationContext', () => ({
  useLocations: () => ({ status: 'ready', location: null, locationOff: false, country: 'US', near: undefined }),
}));
jest.mock('../src/services/api', () => {
  class MockServerError extends Error {
    status: number;
    constructor(message: string, status: number) {
      super(message);
      this.status = status;
    }
  }
  return {
    ServerError: MockServerError,
    ApiService: { getNextBid: jest.fn(), placeBid: jest.fn() },
  };
});
jest.mock('../src/auth/session', () => ({
  currentUsername: () => 'alice',
  isCurrentUser: (name?: string | null) => name === 'alice',
}));

const getNextBid = ApiService.getNextBid as jest.Mock;
const placeBid = ApiService.placeBid as jest.Mock;

const auction = {
  id: 'a1', title: '2016–2020 Toyota Hilux Front Bumper', startingPrice: 300, currentHighBid: 1000, bidCount: 3, auctionEnd: '', status: 'Live', seller: 'bob',
  item: { id: 'a1', itemType: 'Part', make: 'Toyota', model: 'Hilux', year: 2016, description: '', images: [], displayName: '' },
} as any;

// What BidService returns for current total 1,000 (step 20).
const next = {
  minimumBid: 1020, currentTotal: 1000, bidStep: 20, partPrice: 300, currentHighBid: 1000, bidCount: 3, highBidder: 'carol',
  quickBids: [1020, 1040, 1060, 1120], auctionEnd: '', status: 'Live',
};

const navigation = { goBack: jest.fn(), replace: jest.fn() } as any;

/** The full text of a Text node, including nested Text (e.g. "Current bid $1,000 + step $20 = ..."). */
const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');

const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

const render = async () => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<PlaceBidModal navigation={navigation} route={{ key: 'b', name: 'PlaceBidModal', params: { auction } } as any} />);
  });
  return tree!;
};

const confirm = async (tree: ReactTestRenderer.ReactTestRenderer) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(
    n => (n.type as unknown) === 'Text' && textOf(n).startsWith('CONFIRM BID'),
  );
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

beforeEach(() => {
  getNextBid.mockReset().mockResolvedValue(next);
  placeBid.mockReset();
  navigation.replace.mockReset();
});

test('shows the calculation from the Bid service and pre-fills the next bid', async () => {
  const tree = await render();
  const t = texts(tree);

  expect(t).toContain('Current bid $1,000 + step $20 = Next bid $1,020');
  expect(t).toContain('Part price $300 + your bid $720 = Total $1,020');
  // Quick bids: next, +1, +2 and +5 steps – as returned by the server.
  ['$1,020', '$1,040', '$1,060', '$1,120'].forEach(v => expect(t).toContain(v));
  const input = tree.root.find(n => n.props.accessibilityLabel === 'Bid amount' && n.props.onChangeText);
  expect(input.props.value).toBe('1020');
  // No fixed +250 and no container / clearing-fee lines.
  expect(t).not.toMatch(/clearing|container|port/i);
});

test('places the bid total and opens "You\'re the highest bidder"', async () => {
  placeBid.mockResolvedValue({ id: 'b1', amount: 1020 });
  const tree = await render();

  await confirm(tree);

  expect(placeBid).toHaveBeenCalledWith('a1', 1020);
  expect(navigation.replace).toHaveBeenCalledWith('HighestBidder', expect.anything());
});

test('a bid rejected by the server shows its message (with the next valid amount)', async () => {
  placeBid.mockRejectedValue(new ServerError('Your bid is too low. The next valid bid is 1,040', 400));
  const tree = await render();

  await confirm(tree);

  expect(texts(tree)).toContain('Your bid is too low. The next valid bid is 1,040');
  expect(navigation.replace).not.toHaveBeenCalled();
});

test('a bid below the next minimum is not sent', async () => {
  const tree = await render();
  const input = tree.root.find(n => n.props.accessibilityLabel === 'Bid amount' && n.props.onChangeText);
  await ReactTestRenderer.act(async () => input.props.onChangeText('1010'));

  await confirm(tree);

  expect(placeBid).not.toHaveBeenCalled();
  expect(texts(tree)).toContain('The next valid bid is $1,020');
});

test("the seller can't bid on their own listing", async () => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(
      <PlaceBidModal navigation={navigation} route={{ key: 'b', name: 'PlaceBidModal', params: { auction: { ...auction, seller: 'alice' } } } as any} />,
    );
  });

  expect(texts(tree!)).toContain("You can't bid on your own listing");
  await confirm(tree!);
  expect(placeBid).not.toHaveBeenCalled();
});

test('after the sale the winner gets "You won", other bidders "Auction lost", the seller and non-bidders nothing', () => {
  const { bidOutcome } = require('../src/screens/PartDetailsScreen');
  const sold = { status: 'Sold', seller: 'bob', winner: 'alice' };
  expect(bidOutcome(sold, [{ bidder: 'alice' }])).toBe('AuctionWon');
  expect(bidOutcome({ ...sold, winner: 'carol' }, [{ bidder: 'carol' }, { bidder: 'alice' }])).toBe('AuctionLost');
  expect(bidOutcome({ ...sold, winner: 'carol' }, [{ bidder: 'carol' }])).toBeNull();
  expect(bidOutcome({ ...sold, seller: 'alice', winner: 'carol' }, [{ bidder: 'carol' }])).toBeNull();
  expect(bidOutcome({ status: 'Live', seller: 'bob' }, [{ bidder: 'alice' }])).toBeNull();
});

test("the highest bidder can't outbid themselves", async () => {
  getNextBid.mockResolvedValue({ ...next, highBidder: 'alice' });
  const tree = await render();

  expect(texts(tree)).toContain("You're the highest bidder");
  await confirm(tree);
  expect(placeBid).not.toHaveBeenCalled();
});
