import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import App from '../App';

test('app starts on the Splash screen', async () => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<App />);
  });
  const texts = tree!.root
    .findAll(node => (node.type as unknown) === 'Text')
    .map(node => node.props.children)
    .flat()
    .join(' ');
  expect(texts).toContain('ENTER MARKETPLACE');
  // No way into the marketplace without signing in.
  expect(texts).not.toContain('Set Pickup Port & Garage');
  expect(texts).not.toContain('Guest');
});
