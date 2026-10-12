import React, { useEffect, useState } from 'react';
import { Text, TextStyle, StyleProp } from 'react-native';
import { countdown } from '../utils/time';

// One timer for every countdown on screen (a list of cards doesn't start one interval per card).
const listeners = new Set<(now: number) => void>();
let timer: ReturnType<typeof setInterval> | null = null;

function subscribe(listener: (now: number) => void) {
  listeners.add(listener);
  if (!timer) timer = setInterval(() => listeners.forEach(l => l(Date.now())), 1000);
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0 && timer) {
      clearInterval(timer);
      timer = null;
    }
  };
}

/** The current time, updated every second. */
export function useNow(): number {
  const [now, setNow] = useState(Date.now);
  useEffect(() => subscribe(setNow), []);
  return now;
}

/** Live time left until `end`, ticking every second, in hours only: "05:42:09", "53:42:09", "Ended". */
export const Countdown: React.FC<{ end: string; prefix?: string; style?: StyleProp<TextStyle> }> = ({ end, prefix = '', style }) => {
  const now = useNow();
  const text = countdown(end, now);
  return (
    <Text style={style} accessibilityLabel={text === 'Ended' ? 'Auction ended' : `Time left ${text}`}>
      {prefix}
      {text}
    </Text>
  );
};
