// Runs once when the Next.js server starts (Next's instrumentation hook).
//
// The API lives on a site4now shared IIS host that stops the app after ~20 minutes without a
// request, which also stops the send queue and scheduled campaigns, and the next visitor waits
// for a cold start. This Node process on Hostinger is long-lived, so it pings the API every
// 5 minutes to keep it running. The GitHub "Keep API awake" workflow stays as a backup.
//
// Production only; set API_KEEPALIVE=off to disable it there.

const INTERVAL_MS = 5 * 60 * 1000;

declare global {
  // Survives module re-evaluation so timers don't stack.
  var __waApiKeepAlive: ReturnType<typeof setInterval> | undefined;
}

export async function register() {
  if (process.env.NEXT_RUNTIME !== "nodejs") return;
  if (process.env.NODE_ENV !== "production" || process.env.API_KEEPALIVE === "off") return;
  if (globalThis.__waApiKeepAlive) return;

  // Same default as the /api rewrite in next.config.js.
  const base = process.env.API_URL ?? "https://whatsappapi.alamalhospitaljo.com";
  const ping = async () => {
    try {
      await fetch(`${base}/api/health`, { cache: "no-store", signal: AbortSignal.timeout(60_000) });
    } catch {
      // Best effort: a failed ping just means the next one tries again.
    }
  };

  const timer = setInterval(ping, INTERVAL_MS);
  // Never keep the process alive on its own account (graceful shutdown).
  timer.unref?.();
  globalThis.__waApiKeepAlive = timer;
  void ping();
}
