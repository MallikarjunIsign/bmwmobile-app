import { useCallback, useEffect, useRef, useState } from 'react';

/** Countdown in seconds (for "Resend in 30s"). */
export function useCountdown(initialSeconds: number) {
  const [seconds, setSeconds] = useState(initialSeconds);
  const timer = useRef<ReturnType<typeof setInterval> | null>(null);

  const stop = () => {
    if (timer.current) clearInterval(timer.current);
    timer.current = null;
  };

  const restart = useCallback((from: number) => {
    stop();
    setSeconds(from);
    if (from <= 0) return;
    timer.current = setInterval(() => {
      setSeconds(s => {
        if (s <= 1) {
          stop();
          return 0;
        }
        return s - 1;
      });
    }, 1000);
  }, []);

  useEffect(() => {
    restart(initialSeconds);
    return stop;
    // Only on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { seconds, restart };
}
