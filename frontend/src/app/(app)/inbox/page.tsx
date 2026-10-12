"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, formatDate, formatPhone, formatSize, qs, upload, type Customer, type InboxResult, type MediaFile, type Message } from "@/lib/api";
import { CustomerForm } from "@/components/customer-form";
import { Avatar, dayKey, dayLabel, listTime, MessageRow, Preview, windowLeft } from "@/components/inbox";

const FILTERS = [
  ["all", "All"],
  ["unanswered", "Awaiting Reply"],
  ["open", "Window Open"],
  ["optedOut", "Opted Out"],
] as const;
type Filter = (typeof FILTERS)[number][0];
const countKey = { all: "all", unanswered: "unanswered", open: "open", optedOut: "optedOut" } as const;

const QUICK = [
  "Your appointment is confirmed",
  "Please arrive 15 minutes early",
  "Bring your ID and insurance card",
  "Fast for 8 hours before the test",
  "شكراً لتواصلك مع مستشفى الأمل",
];

const PAGE = 50;
const TAKE = 100;
const POLL_MS = 8000;

type Toast = { icon: string; text: string; key: number } | null;

export default function InboxPage() {
  return (
    <Suspense fallback={null}>
      <Inbox />
    </Suspense>
  );
}

function Inbox() {
  const params = useSearchParams();
  const router = useRouter();
  const activeId = Number(params.get("c")) || null;

  const [filter, setFilter] = useState<Filter>("all");
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [limit, setLimit] = useState(PAGE);
  const [inbox, setInbox] = useState<InboxResult | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [infoOpen, setInfoOpen] = useState(false);
  const [toast, setToast] = useState<Toast>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  const refresh = useCallback(() => setTick((n) => n + 1), []);
  const notify = useCallback((icon: string, text: string) => setToast({ icon, text, key: Date.now() }), []);

  useEffect(() => {
    const t = setTimeout(() => setQuery(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  // New messages and delivery statuses arrive through webhooks; poll while the tab is visible.
  useEffect(() => {
    const t = setInterval(() => { if (document.visibilityState === "visible") refresh(); }, POLL_MS);
    return () => clearInterval(t);
  }, [refresh]);

  useEffect(() => {
    let cancelled = false;
    api<InboxResult>(`/inbox${qs({ search: query, filter: filter === "all" ? null : filter, pageSize: limit })}`)
      .then((r) => { if (!cancelled) { setInbox(r); setListError(null); } })
      .catch((e) => { if (!cancelled) setListError(e.message); });
    return () => { cancelled = true; };
  }, [query, filter, limit, tick]);

  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 2600);
    return () => clearTimeout(t);
  }, [toast]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && infoOpen && !document.querySelector("[role=dialog]")) setInfoOpen(false);
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") { e.preventDefault(); searchRef.current?.focus(); }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [infoOpen]);

  const openChat = (id: number) => router.replace(`/inbox?c=${id}`, { scroll: false });
  const closeChat = () => { setInfoOpen(false); router.replace("/inbox", { scroll: false }); };
  const changeFilter = (f: Filter) => { setFilter(f); setLimit(PAGE); setInbox(null); };

  function onTabKey(e: React.KeyboardEvent) {
    if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
    const i = FILTERS.findIndex(([k]) => k === filter);
    const next = FILTERS[(i + (e.key === "ArrowRight" ? 1 : -1) + FILTERS.length) % FILTERS.length][0];
    changeFilter(next);
    requestAnimationFrame(() => document.querySelector<HTMLButtonElement>(`.ftab[data-f="${next}"]`)?.focus());
  }

  return (
    <div className={`ibx ${activeId ? "chat-open" : ""} ${infoOpen && activeId ? "info-open" : ""}`}>
      <section className="panel inbox" aria-labelledby="inboxTitle">
        <div className="list-head">
          <div className="list-title">
            <div>
              <h1 id="inboxTitle">Inbox</h1>
              <p translate="no">Al Amal Hospital WhatsApp</p>
            </div>
            <Link href="/send" className="btn-icon btn-new" aria-label="New Conversation" title="New conversation (starts with an approved template)">
              <i className="ph ph-plus" aria-hidden="true" />
            </Link>
          </div>
          <label className="search">
            <span className="sr-only">Search conversations</span>
            <i className="ph ph-magnifying-glass" aria-hidden="true" />
            <input ref={searchRef} type="search" autoComplete="off" spellCheck={false} placeholder="Search name, file number or phone…"
              value={search} onChange={(e) => { setSearch(e.target.value); setLimit(PAGE); }} />
          </label>
        </div>
        <div className="tabs" role="tablist" aria-label="Filter conversations" onKeyDown={onTabKey}>
          {FILTERS.map(([k, label]) => (
            <button key={k} type="button" className="ftab" role="tab" data-f={k} aria-selected={filter === k}
              tabIndex={filter === k ? 0 : -1} onClick={() => changeFilter(k)}>
              {label}{inbox && <span className="n num">{inbox.counts[countKey[k]]}</span>}
            </button>
          ))}
        </div>
        <div className="conv-list" role="list">
          <ConversationList inbox={inbox} error={listError} activeId={activeId} query={query} filter={filter}
            onOpen={openChat} onClear={() => { setSearch(""); setQuery(""); changeFilter("all"); }}
            onMore={() => setLimit((n) => Math.min(n + PAGE, 200))} />
        </div>
      </section>

      {activeId ? (
        <Conversation key={activeId} id={activeId} tick={tick} infoOpen={infoOpen}
          onToggleInfo={() => setInfoOpen((o) => !o)} onBack={closeChat} onActivity={refresh} notify={notify} />
      ) : (
        <main className="panel convo" aria-label="Conversation">
          <div className="welcome">
            <div>
              <div className="mark" aria-hidden="true"><i className="ph-fill ph-heartbeat" /></div>
              <h2>Pick a Conversation</h2>
              <p>Replies go out from the hospital&apos;s verified WhatsApp Business number.</p>
            </div>
          </div>
        </main>
      )}

      <div className={`ibx-toast ${toast ? "show" : ""}`} role="status" aria-live="polite">
        {toast && <><i className={`ph ${toast.icon}`} aria-hidden="true" />{toast.text}</>}
      </div>
    </div>
  );
}

function ConversationList({ inbox, error, activeId, query, filter, onOpen, onClear, onMore }: {
  inbox: InboxResult | null;
  error: string | null;
  activeId: number | null;
  query: string;
  filter: Filter;
  onOpen: (id: number) => void;
  onClear: () => void;
  onMore: () => void;
}) {
  if (!inbox) {
    if (error) return <div className="empty-list" role="alert"><i className="ph ph-wifi-slash" aria-hidden="true" />{error}</div>;
    return (
      <>
        {Array.from({ length: 6 }, (_, i) => (
          <div key={i} className="sk" aria-hidden="true"><b className="a" /><div><b className="l1" /><b className="l2" /></div></div>
        ))}
        <span className="sr-only">Loading conversations…</span>
      </>
    );
  }

  if (!inbox.items.length) {
    return (
      <div className="empty-list">
        <i className="ph ph-chat-slash" aria-hidden="true" />
        {query
          ? <>No patients match “{query}”. Check the spelling or search by file number.</>
          : filter !== "all" ? "No conversations in this filter right now."
          : "No conversations yet. Messages from patients and anything you send show up here."}
        {(query || filter !== "all") && <><br /><button type="button" onClick={onClear}>Show All Conversations</button></>}
        {!query && filter === "all" && <><br /><Link href="/send">Send the First Message</Link></>}
      </div>
    );
  }

  return (
    <>
      {inbox.items.map(({ customer: c, last, unanswered }) => {
        const when = last?.createdAt ?? c.lastMessageAt ?? c.lastInboundAt;
        return (
          <button key={c.id} type="button" role="listitem" className={`conv ${unanswered ? "unread" : ""}`}
            aria-current={c.id === activeId} onClick={() => onOpen(c.id)}>
            <Avatar customer={c} />
            <div>
              <div className="top">
                <span className="name" dir="auto">{c.name}</span>
                {when && <span className="when num">{listTime(when)}</span>}
              </div>
              <div className="bottom">
                <span className="preview"><Preview last={last} /></span>
                {c.optedOut && <i className="ph-fill ph-prohibit flag-out" aria-label="Opted out" />}
                {unanswered > 0 && <span className="pill-count num" aria-label={`${unanswered} waiting for a reply`}>{unanswered}</span>}
              </div>
            </div>
          </button>
        );
      })}
      {inbox.total > inbox.items.length && inbox.items.length < 200 && (
        <button type="button" className="more" onClick={onMore}>Show More Conversations</button>
      )}
    </>
  );
}

function Conversation({ id, tick, infoOpen, onToggleInfo, onBack, onActivity, notify }: {
  id: number;
  tick: number;
  infoOpen: boolean;
  onToggleInfo: () => void;
  onBack: () => void;
  onActivity: () => void;
  notify: (icon: string, text: string) => void;
}) {
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [recent, setRecent] = useState<Message[] | null>(null);
  const [older, setOlder] = useState<Message[]>([]);
  const [hasOlder, setHasOlder] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [localTick, setLocalTick] = useState(0);
  const [editing, setEditing] = useState(false);
  const stream = useRef<HTMLDivElement>(null);
  const lastId = useRef<number | null>(null);
  const firstLoad = useRef(true);
  // Messages shown at first load (and older pages) appear without the entry animation.
  const [known, setKnown] = useState<Set<number> | null>(null);
  const keepScroll = useRef<number | null>(null);

  const reload = useCallback(() => setLocalTick((n) => n + 1), []);

  useEffect(() => {
    let cancelled = false;
    Promise.all([api<Customer>(`/customers/${id}`), api<Message[]>(`/customers/${id}/messages?take=${TAKE}`)])
      .then(([c, list]) => {
        if (cancelled) return;
        setCustomer(c);
        setRecent(list);
        if (firstLoad.current) {
          firstLoad.current = false;
          setHasOlder(list.length === TAKE);
          setKnown(new Set(list.map((m) => m.id)));
        }
        setError(null);
      })
      .catch((e) => { if (!cancelled) setError(e instanceof ApiError && e.status === 404 ? "This conversation no longer exists." : e.message); });
    return () => { cancelled = true; };
  }, [id, tick, localTick]);

  const recentIds = new Set(recent?.map((m) => m.id));
  const messages = recent ? [...older.filter((m) => !recentIds.has(m.id)), ...recent] : null;

  // Stick to the bottom when a new message arrives; keep the position when older ones load.
  useLayoutEffect(() => {
    const s = stream.current;
    if (!s || !messages) return;
    if (keepScroll.current !== null) {
      s.scrollTop = s.scrollHeight - keepScroll.current;
      keepScroll.current = null;
      return;
    }
    const newest = messages.at(-1)?.id ?? null;
    if (newest !== lastId.current) {
      s.scrollTop = s.scrollHeight;
      lastId.current = newest;
    }
  });

  async function loadOlder() {
    const first = messages?.[0];
    if (!first) return;
    try {
      const list = await api<Message[]>(`/customers/${id}/messages?take=${TAKE}&before=${first.id}`);
      keepScroll.current = stream.current ? stream.current.scrollHeight - stream.current.scrollTop : null;
      setOlder((o) => [...list, ...o]);
      setKnown((k) => new Set([...(k ?? []), ...list.map((m) => m.id)]));
      setHasOlder(list.length === TAKE);
    } catch (e) {
      notify("ph-warning-circle", e instanceof ApiError ? e.message : "Could not load older messages.");
    }
  }

  async function retry(m: Message) {
    try {
      await api(`/messages/${m.id}/resend`, { method: "POST" });
      notify("ph-arrows-clockwise", "Message queued again");
      reload();
      onActivity();
    } catch (e) {
      notify("ph-warning-circle", e instanceof ApiError ? e.message : "Could not resend.");
    }
  }

  function onSent(m: Message) {
    setRecent((r) => (r ? [...r, m] : [m]));
    reload();
    onActivity();
  }

  if (!customer || !messages) {
    return (
      <main className="panel convo" aria-label="Conversation">
        <header className="convo-head">
          <button type="button" className="btn-icon back" onClick={onBack} aria-label="Back to Inbox"><i className="ph ph-caret-left" aria-hidden="true" /></button>
        </header>
        <div className="welcome"><p role={error ? "alert" : undefined}>{error ?? "Loading conversation…"}</p></div>
      </main>
    );
  }

  const left = windowLeft(customer);
  const canReply = customer.canReply && !customer.optedOut;
  const status = customer.optedOut ? <span className="chip danger"><i className="ph ph-prohibit" aria-hidden="true" />Opted Out</span>
    : left ? <span className="chip open"><i className="ph ph-chat-circle-dots" aria-hidden="true" />Window Open</span>
    : <span className="chip"><i className="ph ph-clock-countdown" aria-hidden="true" />Template Only</span>;

  let prev: Message | null = null;
  let lastDay: string | null = null;
  const rows: React.ReactNode[] = [];
  for (const m of messages) {
    const day = dayKey(m.createdAt);
    if (day !== lastDay) {
      rows.push(<div key={`d-${day}`} className="day">{dayLabel(m.createdAt)}</div>);
      lastDay = day;
      prev = null;
    }
    const first = !prev || prev.direction !== m.direction
      || new Date(m.createdAt).getTime() - new Date(prev.createdAt).getTime() > 10 * 60000;
    rows.push(<MessageRow key={m.id} m={m} first={first} enter={!!known && !known.has(m.id)} onRetry={retry} />);
    prev = m;
  }

  return (
    <>
      <main className="panel convo" aria-label={`Conversation with ${customer.name}`}>
        <header className="convo-head">
          <button type="button" className="btn-icon back" onClick={onBack} aria-label="Back to Inbox"><i className="ph ph-caret-left" aria-hidden="true" /></button>
          <button type="button" className="who" onClick={onToggleInfo} aria-label={`Open patient details for ${customer.name}`}>
            <Avatar customer={customer} size="s" />
            <div>
              <div className="nm" dir="auto">{customer.name}</div>
              <div className="st num">
                <span dir="ltr">{formatPhone(customer.phone)}</span>{customer.fileNumber && `, File ${customer.fileNumber}`}
              </div>
            </div>
          </button>
          {status}
          <Link href={`/send?customer=${customer.id}`} className="btn-icon hide-sm" aria-label="Send Template" title="Send Template">
            <i className="ph ph-note" aria-hidden="true" />
          </Link>
          <button type="button" className="btn-icon" onClick={onToggleInfo} aria-label="Patient Details" title="Patient Details" aria-expanded={infoOpen}>
            <i className="ph ph-sidebar-simple" aria-hidden="true" />
          </button>
        </header>

        {customer.optedOut ? (
          <div className="window danger" role="note"><i className="ph ph-prohibit" aria-hidden="true" />Patient opted out of WhatsApp. They can reply START to subscribe again.</div>
        ) : left ? (
          <div className="window" role="note"><i className="ph ph-clock" aria-hidden="true" />Free-form replies open for <span className="num">{left}</span></div>
        ) : (
          <div className="window closed" role="note"><i className="ph ph-clock-countdown" aria-hidden="true" />24-hour window closed. Send an approved template to reopen it.</div>
        )}

        <div className="stream" ref={stream} aria-live="polite" aria-relevant="additions">
          {hasOlder
            ? <button type="button" className="older" onClick={loadOlder}>Load Earlier Messages</button>
            : <p className="note"><i className="ph ph-chats-circle" aria-hidden="true" />{messages.length ? `Start of your conversation with ${customer.name}.` : "No messages yet. Start with an approved template."}</p>}
          {rows}
        </div>

        <Composer customer={customer} canReply={canReply} onSent={onSent} />
      </main>

      {infoOpen && (
        <InfoPanel customer={customer} messages={messages} onClose={onToggleInfo} onEdit={() => setEditing(true)} notify={notify}
          onChanged={(c) => { setCustomer(c); onActivity(); }} />
      )}

      {editing && (
        <CustomerForm customer={customer} onClose={() => setEditing(false)}
          onSaved={(c) => { setCustomer(c); setEditing(false); onActivity(); notify("ph-check-circle", "Patient details saved"); }}
          onDeleted={() => { setEditing(false); onBack(); onActivity(); }} />
      )}
    </>
  );
}

function Composer({ customer, canReply, onSent }: { customer: Customer; canReply: boolean; onSent: (m: Message) => void }) {
  const [text, setText] = useState("");
  const [file, setFile] = useState<MediaFile | null>(null);
  const [busy, setBusy] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ta = useRef<HTMLTextAreaElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  useLayoutEffect(() => {
    const el = ta.current;
    if (!el) return;
    el.style.height = "42px";
    el.style.height = `${Math.min(el.scrollHeight, 140)}px`;
  }, [text]);

  async function onFile(list: FileList | null) {
    if (!list?.length) return;
    setUploading(true);
    setError(null);
    try {
      const form = new FormData();
      form.append("files", list[0]);
      setFile((await upload<MediaFile[]>("/media", form))[0]);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Upload failed.");
    } finally {
      setUploading(false);
      if (fileInput.current) fileInput.current.value = "";
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (busy || (!text.trim() && !file)) return;
    setBusy(true);
    setError(null);
    try {
      const m = await api<Message>(`/customers/${customer.id}/reply`, { method: "POST", body: { text, mediaFileId: file?.id } });
      setText("");
      setFile(null);
      onSent(m);
      ta.current?.focus();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not send.");
    } finally {
      setBusy(false);
    }
  }

  const hasContent = !!text.trim() || !!file;
  return (
    <div className="dock">
      <div className="quick" aria-label={canReply ? "Quick replies" : "Approved templates"}>
        {canReply
          ? QUICK.map((q) => (
            <button key={q} type="button" dir="auto" onClick={() => { setText(q); ta.current?.focus(); }}>{q}</button>
          ))
          : !customer.optedOut && (
            <Link href={`/send?customer=${customer.id}`} className="primary"><i className="ph ph-note" aria-hidden="true" />Send an Approved Template</Link>
          )}
      </div>
      {error && <p className="compose-err" role="alert">{error}</p>}
      {file && (
        <div className="attached">
          <i className="ph ph-paperclip" aria-hidden="true" />
          <span>{file.fileName}</span><small>{formatSize(file.size)}</small>
          <button type="button" onClick={() => setFile(null)} aria-label={`Remove ${file.fileName}`}><i className="ph ph-x" aria-hidden="true" /></button>
        </div>
      )}
      <form className="compose" onSubmit={onSubmit}>
        <div className="field">
          <label className="sr-only" htmlFor="composer-input">Message to {customer.name}</label>
          <textarea id="composer-input" ref={ta} rows={1} dir="auto" autoComplete="off" maxLength={4096} disabled={!canReply}
            placeholder={customer.optedOut ? "This patient opted out of WhatsApp messages" : canReply ? "Write a message…" : "Choose a template above to message this patient…"}
            value={text} onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); e.currentTarget.form?.requestSubmit(); } }} />
          <input ref={fileInput} type="file" hidden onChange={(e) => onFile(e.target.files)}
            accept=".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.jpg,.jpeg,.png,.mp4,.3gp,.mp3,.m4a,.aac,.amr,.ogg" />
          <button type="button" className="btn-icon" aria-label={uploading ? "Uploading file" : "Attach File"} title="Attach File"
            disabled={!canReply || uploading} onClick={() => fileInput.current?.click()}>
            <i className={`ph ${uploading ? "ph-spinner-gap" : "ph-paperclip"}`} aria-hidden="true" />
          </button>
        </div>
        <button className="send" type="submit" aria-label="Send Message" disabled={!canReply || !hasContent || busy || uploading}>
          <i className="ph-fill ph-paper-plane-right" aria-hidden="true" />
        </button>
      </form>
    </div>
  );
}

function InfoPanel({ customer: c, messages, onClose, onEdit, onChanged, notify }: {
  customer: Customer;
  messages: Message[];
  onClose: () => void;
  onEdit: () => void;
  onChanged: (c: Customer) => void;
  notify: (icon: string, text: string) => void;
}) {
  const [confirmOptOut, setConfirmOptOut] = useState(false);
  const [busy, setBusy] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);
  const files = messages.filter((m) => m.hasMedia).reverse();
  const left = windowLeft(c);

  useEffect(() => { closeRef.current?.focus(); }, []);

  async function setOptedOut(optedOut: boolean) {
    setBusy(true);
    try {
      const updated = await api<Customer>(`/customers/${c.id}`, {
        method: "PUT",
        body: { name: c.name, phone: `+${c.phone}`, fileNumber: c.fileNumber, notes: c.notes, optedOut },
      });
      onChanged(updated);
      notify("ph-info", optedOut ? "Patient opted out of WhatsApp messages" : "Patient opted back in");
    } catch (e) {
      notify("ph-warning-circle", e instanceof ApiError ? e.message : "Could not update the patient.");
    } finally {
      setBusy(false);
      setConfirmOptOut(false);
    }
  }

  return (
    <aside className="panel info-panel" aria-label="Patient details">
      <div className="info-bar">
        <h2>Patient Details</h2>
        <button ref={closeRef} type="button" className="btn-icon" onClick={onClose} aria-label="Close Patient Details"><i className="ph ph-x" aria-hidden="true" /></button>
      </div>
      <div className="profile">
        <Avatar customer={c} size="xl" />
        <h3 dir="auto">{c.name}</h3>
        <p className="num" dir="ltr">{formatPhone(c.phone)}</p>
        {c.optedOut ? <span className="chip danger"><i className="ph ph-prohibit" aria-hidden="true" />Opted Out</span>
          : left ? <span className="chip open"><i className="ph ph-chat-circle-dots" aria-hidden="true" />Replies open for {left}</span>
          : <span className="chip"><i className="ph ph-clock-countdown" aria-hidden="true" />Template Only</span>}
        <div className="acts">
          <a href={`tel:+${c.phone}`}><i className="ph ph-phone" aria-hidden="true" />Call</a>
          <Link href={`/send?customer=${c.id}`}><i className="ph ph-note" aria-hidden="true" />Template</Link>
          <button type="button" onClick={onEdit}><i className="ph ph-pencil-simple" aria-hidden="true" />Edit</button>
        </div>
      </div>
      <div className="sec">
        <h4>Patient Record</h4>
        <dl className="kv">
          <dt>File number</dt><dd className="num" translate="no">{c.fileNumber ?? "Not set"}</dd>
          <dt>Phone</dt><dd className="num" dir="ltr">{formatPhone(c.phone)}</dd>
          <dt>Last message</dt><dd>{c.lastInboundAt ? formatDate(c.lastInboundAt) : "Never wrote to us"}</dd>
          <dt>Patient since</dt><dd>{formatDate(c.createdAt)}</dd>
          {c.optedOut && <><dt>Opted out</dt><dd>{formatDate(c.optedOutAt)}</dd></>}
        </dl>
      </div>
      <div className="sec">
        <h4>Notes</h4>
        {c.notes ? <p className="muted" dir="auto" style={{ whiteSpace: "pre-wrap", color: "var(--ink)" }}>{c.notes}</p>
          : <p className="muted">No notes. Use Edit to add some.</p>}
      </div>
      <div className="sec">
        <h4>Shared Files</h4>
        {files.length ? (
          <div className="links">
            {files.map((m) => (
              <a key={m.id} href={`/api/messages/${m.id}/media`} target="_blank" rel="noopener">
                <i className={`ph ${m.type === "image" ? "ph-image" : m.type === "audio" ? "ph-microphone" : m.type === "video" ? "ph-video-camera" : "ph-file-text"}`} aria-hidden="true" />
                <span dir="auto">{m.mediaFileName ?? `${m.type} from ${formatDate(m.createdAt)}`}</span>
              </a>
            ))}
          </div>
        ) : <p className="muted">Files you send or receive in this chat show up here.</p>}
      </div>
      <div className="sec">
        <div className="links">
          <Link href={`/customers/${c.id}`}><i className="ph ph-clock-counter-clockwise" aria-hidden="true" /><span>Open Full Patient Page</span></Link>
          {c.optedOut ? (
            <button type="button" onClick={() => setOptedOut(false)} disabled={busy}>
              <i className="ph ph-check-circle" aria-hidden="true" /><span>Opt Back In to WhatsApp</span>
            </button>
          ) : (
            <button type="button" className="danger" disabled={busy}
              onClick={() => (confirmOptOut ? setOptedOut(true) : setConfirmOptOut(true))} onBlur={() => setConfirmOptOut(false)}>
              <i className="ph ph-prohibit" aria-hidden="true" /><span>{confirmOptOut ? "Click Again to Confirm Opt-Out" : "Opt Out of WhatsApp"}</span>
            </button>
          )}
        </div>
      </div>
    </aside>
  );
}
