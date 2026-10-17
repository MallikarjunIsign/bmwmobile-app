import React from 'react';
import { Alert, Linking } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { OrderTrackingCard } from '../src/components/OrderTrackingCard';
import { PaymentsApi } from '../src/services/paymentsApi';
import { formatMoney } from '../src/utils/geo';
import type { Order } from '../src/types/listing';

jest.mock('../src/location/LocationContext', () => ({
  useLocations: () => ({ country: 'US', location: null, addresses: [], usingCurrentLocation: true }),
}));
jest.mock('../src/services/paymentsApi', () => {
  const actual = jest.requireActual('../src/services/paymentsApi');
  return { ...actual, PaymentsApi: { reportProblem: jest.fn(), cancelAndRefund: jest.fn(), payOrder: jest.fn() } };
});
jest.mock('../src/services/marketApi', () => ({ MarketApi: { order: jest.fn(), listingDetail: jest.fn(), orderStep: jest.fn() } }));

const api = PaymentsApi as unknown as Record<string, jest.Mock>;
const usd = (n: number, decimals = 0) => formatMoney(n, 'US', decimals);

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) => tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.findAll(n => (n.type as unknown) === 'Text' && textOf(n).startsWith(label))[0];
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const render = async (el: React.ReactElement) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(el);
  });
  mounted.push(tree!);
  return tree!;
};
afterEach(async () => {
  for (const t of mounted.splice(0)) await ReactTestRenderer.act(async () => t.unmount());
  jest.restoreAllMocks();
});
beforeEach(() => Object.values(api).forEach(m => m.mockReset()));

const steps = [
  { status: 'SoldAwaitingHandover', label: 'Sold', done: true, current: false, at: null, note: null },
  { status: 'ReadyForPickup', label: 'Ready for pickup', done: true, current: false, at: null, note: null },
  { status: 'PickedUp', label: 'Picked up', done: true, current: true, at: null, note: null },
  { status: 'Completed', label: 'Completed', done: false, current: false, at: null, note: null },
];
const order = (patch: Partial<Order>): Order => ({
  id: 'o1', auctionId: 'a1', role: 'buyer', seller: 'bob', buyer: 'alice', otherParty: 'bob', title: 'Dash cam',
  finalTotal: 120, partPrice: 120, winningBid: 0, total: 120, countryCode: 'US', deliveryOptions: ['Pickup'], deliveryMethod: 'Pickup',
  deliveryChoices: [], status: 'PickedUp', statusLabel: 'Picked up', paymentStatus: 'Paid', tracking: steps as Order['tracking'],
  actions: [], createdAt: '', ...patch,
});

describe('after paying: buyer', () => {
  test('receipt link, auto-complete date and Report a problem → on hold', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockImplementation(async () => undefined);
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    const onChange = jest.fn();
    api.reportProblem.mockResolvedValue(order({ onHold: true, holdReason: 'Buyer reported a problem: Damaged: cracked lens', canReportProblem: false }));

    const tree = await render(<OrderTrackingCard money={usd} onChange={onChange} onOpenListing={jest.fn()}
      order={order({ receiptUrl: 'https://pay.stripe.com/receipts/x', autoCompleteAt: '2026-10-23T10:00:00Z', canReportProblem: true })} />);
    expect(texts(tree)).toContain('✓ Paid online');
    expect(texts(tree)).toMatch(/Completes automatically on .*23.* and the seller is paid/);

    await press(tree, '🧾 View receipt');
    expect(open).toHaveBeenCalledWith('https://pay.stripe.com/receipts/x');

    await press(tree, '⚠️ Report a problem');
    expect(texts(tree)).toContain("What's wrong?");
    await press(tree, 'Damaged');
    const input = tree.root.findAll(n => (n.type as unknown) === 'TextInput' && n.props.placeholder === 'Details (optional)')[0];
    await ReactTestRenderer.act(async () => input.props.onChangeText('cracked lens'));
    await press(tree, 'Report problem');
    expect(api.reportProblem).toHaveBeenCalledWith('o1', 'Damaged: cracked lens');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ onHold: true }));
    expect(alert).toHaveBeenCalledWith('Problem reported', expect.stringContaining('on hold'));
    // The first render of the card is slow under a loaded full run.
  }, 20000);

  test('on hold: shown, no auto-complete line; refunds are shown', async () => {
    const tree = await render(<OrderTrackingCard money={usd} onChange={jest.fn()} onOpenListing={jest.fn()}
      order={order({ onHold: true, holdReason: 'Buyer reported a problem: Late', autoCompleteAt: '2026-10-23T10:00:00Z', refundedCents: 2050 })} />);
    expect(texts(tree)).toContain('⏸ On hold – Buyer reported a problem: Late');
    expect(texts(tree)).not.toContain('Completes automatically');
    expect(texts(tree)).toContain('↩ Refunded $20.50 to your card');
  });

  test('a failed report shows the server message, not "Problem reported"', async () => {
    const { ServerError } = jest.requireActual('../src/services/api');
    api.reportProblem.mockRejectedValue(new ServerError('This order is already closed – contact support', 409));
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    const tree = await render(<OrderTrackingCard money={usd} onChange={jest.fn()} onOpenListing={jest.fn()} order={order({ canReportProblem: true })} />);
    await press(tree, '⚠️ Report a problem');
    await press(tree, 'Report problem');
    expect(alert).toHaveBeenCalledWith('Not updated', 'This order is already closed – contact support');
    expect(alert).not.toHaveBeenCalledWith('Problem reported', expect.anything());
  });
});

describe('after paying: seller', () => {
  test('the payout line – "about €…" for an Irish seller', async () => {
    const tree = await render(<OrderTrackingCard money={(n, d = 0) => formatMoney(n, 'IE', d)} onChange={jest.fn()} onOpenListing={jest.fn()}
      order={order({ role: 'seller', countryCode: 'IE', payout: { amountCents: 9405, currency: 'EUR', approximate: true, status: 'Scheduled',
        label: "You'll receive about €94.05 when the buyer confirms, or automatically on 23 Oct" } })} />);
    expect(texts(tree)).toContain("💰 You'll receive about €94.05 when the buyer confirms, or automatically on 23 Oct");
    expect(texts(tree)).not.toContain('View receipt');
    expect(texts(tree)).not.toContain('Report a problem');
  });

  test('Cancel and refund (before pickup) asks first, then refunds', async () => {
    let buttons: { text?: string; onPress?: () => void }[] = [];
    jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, b) => {
      buttons = (b ?? []) as typeof buttons;
    });
    const onChange = jest.fn();
    api.cancelAndRefund.mockResolvedValue(order({ role: 'seller', status: 'Cancelled', paymentStatus: 'Refunded', refundedCents: 12000 }));
    const tree = await render(<OrderTrackingCard money={usd} onChange={onChange} onOpenListing={jest.fn()}
      order={order({ role: 'seller', status: 'SoldAwaitingHandover', canCancelAndRefund: true })} />);

    await press(tree, 'Cancel and refund');
    expect(api.cancelAndRefund).not.toHaveBeenCalled();
    await ReactTestRenderer.act(async () => buttons.find(b => b.text === 'Cancel and refund')!.onPress!());
    expect(api.cancelAndRefund).toHaveBeenCalledWith('o1', expect.any(String));
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ status: 'Cancelled', paymentStatus: 'Refunded' }));
  });
});
