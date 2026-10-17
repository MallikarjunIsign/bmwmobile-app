import React from 'react';
import { Linking } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { SellerPaymentsCard } from '../src/components/SellerPaymentsCard';
import { PaymentsApi, sellerPaymentView, type SellerPaymentAccount } from '../src/services/paymentsApi';

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return { useFocusEffect: (fn: () => void) => useEffect(fn, [fn]) };
});
jest.mock('../src/services/paymentsApi', () => {
  const actual = jest.requireActual('../src/services/paymentsApi');
  return {
    ...actual,
    PaymentsApi: { ensureSellerAccount: jest.fn(), sellerAccount: jest.fn(), onboardingLink: jest.fn(), dashboardLink: jest.fn() },
  };
});

const api = PaymentsApi as unknown as Record<string, jest.Mock>;
const account = (patch: Partial<SellerPaymentAccount>): SellerPaymentAccount => ({
  available: true, country: 'US', status: 'NotStarted', message: 'Set up payments to sell.', canPublish: false, requirementsDue: 0, ...patch,
});

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

const render = async () => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<SellerPaymentsCard />);
  });
  return tree!;
};
const press = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && textOf(n) === label);
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

beforeEach(() => Object.values(api).forEach(m => m.mockReset()));

test('status labels and the button for each state', () => {
  expect(sellerPaymentView(account({ status: 'NotStarted' }))).toMatchObject({ label: 'Not started', action: 'SET UP PAYMENTS' });
  expect(sellerPaymentView(account({ status: 'Pending' }))).toMatchObject({ label: 'Pending verification', action: null });
  expect(sellerPaymentView(account({ status: 'ActionNeeded' }))).toMatchObject({ label: 'Action needed', action: 'CONTINUE SETUP' });
  expect(sellerPaymentView(account({ status: 'Active' }))).toMatchObject({ label: 'Active', action: null });
  expect(sellerPaymentView(account({ status: 'NotAvailable' }))).toMatchObject({ label: 'Coming soon', action: null });
});

test('first visit opens the account; SET UP PAYMENTS opens Stripe\'s hosted setup in the browser', async () => {
  api.ensureSellerAccount.mockResolvedValue(account({}));
  api.onboardingLink.mockResolvedValue('https://connect.stripe.com/setup/e/acct_1/abc');
  const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);

  const tree = await render();
  expect(api.ensureSellerAccount).toHaveBeenCalled();
  expect(texts(tree)).toContain('Not started');
  expect(texts(tree)).toContain('You can publish new listings once payments are active.');

  await press(tree, 'SET UP PAYMENTS');
  expect(open).toHaveBeenCalledWith('https://connect.stripe.com/setup/e/acct_1/abc');
  open.mockRestore();
});

test('active: no setup button, the payouts dashboard link', async () => {
  api.ensureSellerAccount.mockResolvedValue(account({ status: 'Active', canPublish: true, message: 'Payments are active.' }));
  api.dashboardLink.mockResolvedValue('https://connect.stripe.com/express/acct_1');
  const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);

  const tree = await render();
  expect(texts(tree)).toContain('Active');
  expect(texts(tree)).not.toContain('SET UP PAYMENTS');
  expect(texts(tree)).not.toContain('You can publish new listings');
  await press(tree, 'Payouts & bank account (Stripe) →');
  expect(open).toHaveBeenCalledWith('https://connect.stripe.com/express/acct_1');
  open.mockRestore();
});

test('India / Nigeria: coming soon, nothing to set up', async () => {
  api.ensureSellerAccount.mockResolvedValue(account({
    available: false, country: 'IN', status: 'NotAvailable', canPublish: true, message: 'Online payment is coming soon in your country.',
  }));
  const tree = await render();
  expect(texts(tree)).toContain('Online payment is coming soon in your country.');
  expect(texts(tree)).not.toContain('SET UP PAYMENTS');
});
