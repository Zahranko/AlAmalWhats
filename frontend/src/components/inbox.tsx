"use client";

import type { Customer, InboxItem, Message, MessageStatus } from "@/lib/api";

/* ---------- Formatting ---------- */

const fmtTime = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
const fmtDay = new Intl.DateTimeFormat(undefined, { weekday: "long", day: "numeric", month: "long" });
const fmtShort = new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" });
const rel = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
const cap = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

function dayDiff(ts: number) {
  const a = new Date(ts); a.setHours(0, 0, 0, 0);
  const b = new Date(); b.setHours(0, 0, 0, 0);
  return Math.round((a.getTime() - b.getTime()) / 864e5);
}

export const timeOf = (iso: string) => fmtTime.format(new Date(iso));
export const dayKey = (iso: string) => new Date(iso).toDateString();
export function dayLabel(iso: string) {
  const ts = new Date(iso).getTime();
  const d = dayDiff(ts);
  return d >= -1 ? cap(rel.format(d, "day")) : fmtDay.format(ts);
}
export function listTime(iso: string) {
  const ts = new Date(iso).getTime();
  const d = dayDiff(ts);
  return d === 0 ? fmtTime.format(ts) : d === -1 ? cap(rel.format(-1, "day")) : fmtShort.format(ts);
}

export const isAr = (s: string | null | undefined) => /[؀-ۿ]/.test(s ?? "");

/** "appointment_reminder" → "Appointment Reminder". */
export const templateTitle = (name: string) => name.split(/[_\s]+/).filter(Boolean).map(cap).join(" ");

/** Time left in Meta's 24-hour free-form window, or null when it is closed. */
export function windowLeft(c: Customer) {
  if (!c.lastInboundAt) return null;
  const mins = Math.round((new Date(c.lastInboundAt).getTime() + 24 * 3600e3 - Date.now()) / 60000);
  return mins > 0 ? `${Math.floor(mins / 60)} h ${mins % 60} min` : null;
}

/* ---------- Avatar ---------- */

const TONES = [
  ["#DCEFF4", "#0B5F76"], ["#E3EEE6", "#2D6A45"], ["#ECE7F6", "#5B4596"],
  ["#F6E9E1", "#94512A"], ["#E6ECF5", "#34507E"], ["#F4E6EE", "#8A3561"], ["#EFEDE2", "#6A5B1E"],
];

function initials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean).map((w) => w.replace(/^(ال|Al-)/i, "") || w);
  if (!words.length) return "?";
  if (isAr(name)) return words[0][0]; // one Arabic letter reads cleaner than a joined pair
  return words.slice(0, 2).map((w) => w[0]).join("").toUpperCase();
}

export function Avatar({ customer, size = "" }: { customer: Customer; size?: "" | "s" | "xl" }) {
  const [bg, fg] = TONES[customer.id % TONES.length];
  return (
    <div className={`avatar ${size}`} lang={isAr(customer.name) ? "ar" : undefined} style={{ background: bg, color: fg }} aria-hidden="true">
      {initials(customer.name)}
    </div>
  );
}

/* ---------- Ticks ---------- */

export function Ticks({ status }: { status: MessageStatus }) {
  switch (status) {
    case "failed": return <i className="ph-fill ph-warning-circle" style={{ color: "var(--danger)" }} aria-label="Not delivered" />;
    case "cancelled": return <i className="ph ph-prohibit" aria-label="Cancelled" />;
    case "queued":
    case "sending": return <i className="ph ph-clock" aria-label="Sending" />;
    case "sent": return <i className="ph ph-check" aria-label="Sent" />;
    case "read": return <i className="ph ph-checks read" aria-label="Read" />;
    default: return <i className="ph ph-checks" aria-label="Delivered" />;
  }
}

/* ---------- List preview ---------- */

const typeIcon: Record<string, string> = {
  image: "ph-image", sticker: "ph-sticker", video: "ph-video-camera", audio: "ph-microphone", document: "ph-file-text",
  location: "ph-map-pin", template: "ph-note", contacts: "ph-user-rectangle",
};

export function Preview({ last }: { last: InboxItem["last"] }) {
  if (!last) return <span className="muted">No messages yet</span>;
  const body = last.type === "template"
    ? templateTitle(last.templateName ?? "Template")
    : last.type === "location" ? "Location"
    : last.body || last.mediaFileName || `${cap(last.type)} message`;
  const icon = typeIcon[last.type];
  return (
    <>
      {last.direction === "out" && <Ticks status={last.status} />}
      {icon && <i className={`ph ${icon}`} aria-hidden="true" />}
      <span dir="auto">{body}</span>
    </>
  );
}

/* ---------- Bubbles ---------- */

const mediaUrl = (m: Message) => `/api/messages/${m.id}/media`;

function fileIcon(name: string | null) {
  const ext = name?.split(".").pop()?.toLowerCase() ?? "";
  if (ext === "pdf") return "ph-file-pdf";
  if (["doc", "docx"].includes(ext)) return "ph-file-doc";
  if (["xls", "xlsx", "csv"].includes(ext)) return "ph-file-xls";
  if (["ppt", "pptx"].includes(ext)) return "ph-file-ppt";
  if (["jpg", "jpeg", "png", "webp"].includes(ext)) return "ph-file-image";
  return "ph-file";
}

function DocCard({ m }: { m: Message }) {
  const name = m.mediaFileName ?? `${cap(m.type)} file`;
  return (
    <div className="doc">
      <div className="fi" aria-hidden="true"><i className={`ph-fill ${fileIcon(m.mediaFileName)}`} /></div>
      <div className="fn"><strong dir="auto">{name}</strong><small>{m.mediaFileName?.split(".").pop()?.toUpperCase() ?? cap(m.type)}</small></div>
      <a href={mediaUrl(m)} target="_blank" rel="noopener" aria-label={`Download ${name}`}><i className="ph ph-download-simple" aria-hidden="true" /></a>
    </div>
  );
}

/** Inbound locations are stored as "📍 lat, lng name". */
function parseLocation(body: string | null) {
  const match = body?.match(/^📍\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\s*(.*)$/u);
  if (!match) return null;
  const lat = Number(match[1]), lng = Number(match[2]);
  const z = 15, n = 2 ** z, rad = (lat * Math.PI) / 180;
  const x = Math.floor(((lng + 180) / 360) * n);
  const y = Math.floor(((1 - Math.asinh(Math.tan(rad)) / Math.PI) / 2) * n);
  return { lat, lng, name: match[3].trim(), tile: `https://tile.openstreetmap.org/${z}/${x}/${y}.png` };
}

function Meta({ m }: { m: Message }) {
  const out = m.direction === "out";
  return (
    <span className="meta num" dir="ltr">
      <time dateTime={m.createdAt}>{timeOf(m.createdAt)}</time>
      {out && <Ticks status={m.status} />}
    </span>
  );
}

export function MessageRow({ m, first, enter, onRetry }: { m: Message; first: boolean; enter: boolean; onRetry: (m: Message) => void }) {
  const out = m.direction === "out";
  const cls = `mrow ${out ? "out" : "in"} ${first ? "first" : ""} ${enter ? "enter" : ""}`;
  const failed = m.status === "failed" && (
    <div className="failed">
      <i className="ph ph-warning-circle" aria-hidden="true" />
      <span>Not delivered{m.error ? `: ${m.error}` : "."}</span>
      <button type="button" onClick={() => onRetry(m)}>Retry</button>
    </div>
  );

  if (m.type === "template") {
    return (
      <>
        <div className={cls}>
          <article className="tpl" aria-label={`Template message: ${m.templateName}`}>
            <div className="tpl-top">
              <div className="mk" aria-hidden="true"><i className="ph-fill ph-heartbeat" /></div>
              <div>
                <strong>{templateTitle(m.templateName ?? "Template")}</strong>
                <small>Approved template{m.sentBy ? `, sent by ${m.sentBy}` : ""}</small>
              </div>
            </div>
            <div className="tpl-body">
              {m.hasMedia && <DocCard m={m} />}
              {m.body && <span className="txt" dir="auto">{m.body}</span>}
              <Meta m={m} />
              <div style={{ clear: "both" }} />
            </div>
          </article>
        </div>
        {failed}
      </>
    );
  }

  let inner;
  const loc = m.type === "location" ? parseLocation(m.body) : null;
  if (loc) {
    inner = (
      <>
        <a className="map" href={`https://www.openstreetmap.org/?mlat=${loc.lat}&mlon=${loc.lng}#map=16/${loc.lat}/${loc.lng}`}
          target="_blank" rel="noopener" aria-label={`Open map${loc.name ? `: ${loc.name}` : ""}`}>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={loc.tile} alt="" width={280} height={140} loading="lazy" />
          <i className="ph-fill ph-map-pin pin" aria-hidden="true" />
          <small>© OpenStreetMap</small>
        </a>
        <div className="loc-t" dir="auto">{loc.name || "Shared location"}</div>
      </>
    );
  } else {
    const media = m.hasMedia && (
      m.type === "image" || m.type === "sticker"
        ? (
          <a href={mediaUrl(m)} target="_blank" rel="noopener" aria-label="Open image">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img className="media-img" src={mediaUrl(m)} alt={m.mediaFileName ?? "Image"} loading="lazy" />
          </a>
        )
        : m.type === "video" ? <video className="media-av" src={mediaUrl(m)} controls preload="none" />
        : m.type === "audio" ? <audio className="media-av" src={mediaUrl(m)} controls preload="none" />
        : <DocCard m={m} />
    );
    inner = (
      <>
        {media}
        {m.body
          ? <span className="txt">{m.body}</span>
          : !m.hasMedia && <span className="txt muted-txt">{cap(m.type)} message</span>}
      </>
    );
  }

  const rtl = isAr(m.body);
  return (
    <>
      <div className={cls}>
        <div className="bubble" dir={rtl ? "rtl" : undefined} lang={rtl ? "ar" : undefined}>
          {out && first && m.sentBy && <div className="by">{m.sentBy}</div>}
          {inner}
          <Meta m={m} />
        </div>
      </div>
      {failed}
    </>
  );
}
