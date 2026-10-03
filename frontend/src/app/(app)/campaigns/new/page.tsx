"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { api, ApiError, upload, type Campaign, type MediaFile, type Template } from "@/lib/api";
import { ErrorText, PageHeader } from "@/components/ui";
import { FileChip, TemplatePreview, UploadButton, needsMedia, templateLabel, type TemplateValues } from "@/components/messaging";

interface Sheet { columns: string[]; rows: string[][] }
/** A variable's value comes from a column, or is the same fixed text for everyone. */
type Source = { column: number } | { text: string };
interface RowError { row: number; phone: string; error: string }

const guess = (columns: string[], ...words: string[]) =>
  columns.findIndex((c) => words.some((w) => c.toLowerCase().includes(w)));

export default function NewCampaignPage() {
  const router = useRouter();
  const [templates, setTemplates] = useState<Template[]>([]);
  const [name, setName] = useState("");
  const [templateId, setTemplateId] = useState<number | null>(null);
  const [sheet, setSheet] = useState<Sheet | null>(null);
  const [sheetName, setSheetName] = useState("");
  const [phoneCol, setPhoneCol] = useState(-1);
  const [nameCol, setNameCol] = useState(-1);
  const [sources, setSources] = useState<Record<string, Source>>({});
  const [fileMode, setFileMode] = useState<"column" | "same">("column");
  const [fileCol, setFileCol] = useState(-1);
  const [files, setFiles] = useState<MediaFile[]>([]);
  const [schedule, setSchedule] = useState("");
  const [skipProblems, setSkipProblems] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rowErrors, setRowErrors] = useState<RowError[]>([]);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<Template[]>("/templates?approved=true").then(setTemplates).catch((e) => setError(e.message));
  }, []);

  const template = templates.find((t) => t.id === templateId) ?? null;
  const vars = useMemo(() => template ? [
    ...template.headerParams.map((p, i) => ({ key: `header-${i}`, part: "header" as const, i, label: `Header {{${p}}}` })),
    ...template.bodyParams.map((p, i) => ({ key: `body-${i}`, part: "body" as const, i, label: `{{${p}}}` })),
    ...template.buttonParams.map((b, i) => ({ key: `buttons-${i}`, part: "buttons" as const, i, label: `Button "${b.text}"` })),
  ] : [], [template]);

  async function onSheet(file: File | undefined) {
    if (!file) return;
    setError(null);
    setBusy(true);
    try {
      const form = new FormData();
      form.append("file", file);
      const s = await upload<Sheet>("/campaigns/parse", form);
      setSheet(s);
      setSheetName(file.name);
      setPhoneCol(Math.max(0, guess(s.columns, "phone", "mobile", "هاتف", "موبايل", "جوال", "رقم")));
      setNameCol(guess(s.columns, "name", "اسم"));
      setFileCol(guess(s.columns, "file", "report", "ملف", "pdf"));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not read the file.");
    } finally {
      setBusy(false);
    }
  }

  const fileByName = useMemo(() => new Map(files.map((f) => [f.fileName.toLowerCase(), f])), [files]);

  const valueOf = (src: Source | undefined, row: string[]) =>
    !src ? "" : "column" in src ? (row[src.column] ?? "").trim() : src.text;

  /** The recipients exactly as they'll be sent, plus a reason for rows that can't be. */
  const recipients = useMemo(() => {
    if (!sheet || !template) return [];
    return sheet.rows.map((row, idx) => {
      const values: TemplateValues = { header: [], body: [], buttons: [] };
      for (const v of vars) values[v.part][v.i] = valueOf(sources[v.key], row);
      let file: MediaFile | undefined;
      let problem: string | null = null;
      if (needsMedia(template)) {
        if (fileMode === "same") file = files[0];
        else {
          const wanted = (row[fileCol] ?? "").trim().toLowerCase();
          file = fileByName.get(wanted) ?? fileByName.get(wanted.split(/[\\/]/).pop() ?? "");
          if (!file) problem = wanted ? `No uploaded file named "${row[fileCol]}"` : "No file name in this row";
        }
        if (!file && !problem) problem = "Upload the file";
      }
      const phone = (row[phoneCol] ?? "").trim();
      if (!phone) problem = "No phone number";
      else if (vars.some((v) => !values[v.part][v.i])) problem ??= "A variable is empty";
      return { row: idx + 1, phone, name: nameCol >= 0 ? row[nameCol] : undefined, values, file, problem };
    });
  }, [sheet, template, vars, sources, phoneCol, nameCol, fileMode, fileCol, files, fileByName]);

  const problems = recipients.filter((r) => r.problem);
  const toSend = skipProblems ? recipients.filter((r) => !r.problem) : recipients;

  async function create() {
    setBusy(true);
    setError(null);
    setRowErrors([]);
    try {
      const c = await api<Campaign>("/campaigns", {
        method: "POST",
        body: {
          name, templateId, scheduledAt: schedule ? new Date(schedule).toISOString() : undefined,
          recipients: toSend.map((r) => ({
            phone: r.phone, name: r.name, header: r.values.header, body: r.values.body, buttons: r.values.buttons, mediaFileId: r.file?.id,
          })),
        },
      });
      router.push(`/campaigns/${c.id}`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create the campaign.");
      // The server lists the rows it rejected.
      const rows = err instanceof ApiError ? (err.data as { rows?: RowError[] } | undefined)?.rows : undefined;
      setRowErrors(rows ?? []);
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader title="New bulk send" description="Send one template to a list of people, each with their own values and files." />
      <div className="max-w-5xl space-y-6">
        <section className="card grid gap-4 p-6 sm:grid-cols-2">
          <div>
            <label className="label" htmlFor="cname">Campaign name</label>
            <input id="cname" className="input" required maxLength={200} dir="auto" placeholder="e.g. Lab results 5 Oct"
              value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <div>
            <label className="label" htmlFor="ctemplate">Template</label>
            <select id="ctemplate" className="input" value={templateId ?? ""} onChange={(e) => { setTemplateId(Number(e.target.value) || null); setSources({}); }}>
              <option value="">Choose a template…</option>
              {templates.map((t) => <option key={t.id} value={t.id}>{templateLabel(t)}</option>)}
            </select>
          </div>
        </section>

        {template && (
          <section className="card space-y-4 p-6">
            <h2 className="font-semibold">1. Recipient list</h2>
            <p className="text-sm text-gray-500">An Excel (.xlsx) or CSV file with a header row: one row per person, a phone number column, and a column for each value that differs per person. Up to 5,000 rows.</p>
            <input type="file" accept=".xlsx,.csv" onChange={(e) => onSheet(e.target.files?.[0])} disabled={busy}
              className="block text-sm file:mr-3 file:rounded-md file:border file:border-gray-300 file:bg-white file:px-3 file:py-2 file:text-sm" />
            {sheet && <p className="text-sm text-gray-600">{sheetName}: {sheet.rows.length} rows, {sheet.columns.length} columns.</p>}
          </section>
        )}

        {template && sheet && (
          <section className="card space-y-4 p-6">
            <h2 className="font-semibold">2. Match the columns</h2>
            <div className="grid gap-4 sm:grid-cols-2">
              <ColumnSelect id="phonecol" label="Phone number" columns={sheet.columns} value={phoneCol} onChange={setPhoneCol} />
              <ColumnSelect id="namecol" label="Name (optional, for new customers)" columns={sheet.columns} value={nameCol} onChange={setNameCol} allowNone />
              {vars.map((v) => (
                <VariableSource key={v.key} id={v.key} label={v.label} columns={sheet.columns} value={sources[v.key]}
                  onChange={(s) => setSources((m) => ({ ...m, [v.key]: s }))} />
              ))}
            </div>

            {needsMedia(template) && (
              <div className="space-y-3 border-t border-gray-100 pt-4">
                <p className="label">{template.headerFormat?.toLowerCase()} for each person</p>
                <div className="flex gap-4 text-sm">
                  <label className="flex items-center gap-2"><input type="radio" checked={fileMode === "column"} onChange={() => setFileMode("column")} /> Different file per person (by file name)</label>
                  <label className="flex items-center gap-2"><input type="radio" checked={fileMode === "same"} onChange={() => setFileMode("same")} /> Same file for everyone</label>
                </div>
                {fileMode === "column" && <ColumnSelect id="filecol" label="Column with the file name" columns={sheet.columns} value={fileCol} onChange={setFileCol} />}
                <div className="flex flex-wrap gap-2">{files.map((f) => <FileChip key={f.id} file={f} onRemove={() => setFiles((l) => l.filter((x) => x.id !== f.id))} />)}</div>
                <UploadButton multiple={fileMode === "column"} label={fileMode === "column" ? "Upload the files" : "Upload the file"}
                  onUploaded={(f) => setFiles((l) => fileMode === "same" ? f.slice(0, 1) : [...l, ...f])} />
                {fileMode === "column" && <p className="text-xs text-gray-500">Select all files at once (up to 100 per upload). Each row&apos;s file name must match an uploaded file name.</p>}
              </div>
            )}
          </section>
        )}

        {template && sheet && recipients.length > 0 && (
          <section className="card space-y-4 p-6">
            <h2 className="font-semibold">3. Check and send</h2>
            <div className="grid gap-6 lg:grid-cols-2">
              <div>
                <p className="mb-2 text-sm text-gray-600">First person ({recipients[0].phone || "no phone"}):</p>
                <TemplatePreview template={template} values={recipients[0].values} fileName={recipients[0].file?.fileName} />
              </div>
              <div className="space-y-2 text-sm">
                <p><span className="font-semibold">{recipients.length - problems.length}</span> ready, <span className={problems.length ? "font-semibold text-red-700" : ""}>{problems.length}</span> with problems.</p>
                {problems.length > 0 && (
                  <ul className="max-h-48 overflow-y-auto rounded-md bg-red-50 p-3 text-xs text-red-800">
                    {problems.slice(0, 100).map((p) => <li key={p.row}>Row {p.row + 1}{p.phone && ` (${p.phone})`}: {p.problem}</li>)}
                  </ul>
                )}
                {problems.length > 0 && (
                  <label className="flex items-center gap-2">
                    <input type="checkbox" checked={skipProblems} onChange={(e) => setSkipProblems(e.target.checked)} />
                    Skip the {problems.length} row(s) with problems and send to the rest
                  </label>
                )}
                <p className="text-xs text-gray-500">People who opted out are skipped automatically.</p>
              </div>
            </div>
            <div>
              <label className="label" htmlFor="cschedule">Send at (optional)</label>
              <input id="cschedule" type="datetime-local" className="input max-w-xs" value={schedule} onChange={(e) => setSchedule(e.target.value)} />
            </div>
            <ErrorText error={error} />
            {rowErrors.length > 0 && (
              <ul className="text-xs text-red-700">{rowErrors.map((r) => <li key={r.row}>Row {r.row}: {r.error}</li>)}</ul>
            )}
            <div className="flex gap-2">
              <button className="btn-primary" disabled={busy || !name.trim() || toSend.length === 0 || (problems.length > 0 && !skipProblems)} onClick={create}>
                {busy ? "Creating…" : schedule ? `Schedule ${toSend.length} messages` : `Send ${toSend.length} messages`}
              </button>
              <Link href="/campaigns" className="btn-secondary">Cancel</Link>
            </div>
          </section>
        )}
        {!template && <ErrorText error={error} />}
      </div>
    </>
  );
}

function ColumnSelect({ id, label, columns, value, onChange, allowNone }: {
  id: string; label: string; columns: string[]; value: number; onChange: (i: number) => void; allowNone?: boolean;
}) {
  return (
    <div>
      <label className="label" htmlFor={id}>{label}</label>
      <select id={id} className="input" value={value} onChange={(e) => onChange(Number(e.target.value))}>
        {allowNone ? <option value={-1}>— none —</option> : value < 0 && <option value={-1}>Choose a column…</option>}
        {columns.map((c, i) => <option key={i} value={i}>{c}</option>)}
      </select>
    </div>
  );
}

function VariableSource({ id, label, columns, value, onChange }: {
  id: string; label: string; columns: string[]; value: Source | undefined; onChange: (s: Source) => void;
}) {
  const isText = !!value && "text" in value;
  return (
    <div>
      <label className="label" htmlFor={id}>{label}</label>
      <div className="flex gap-2">
        <select id={id} className="input" value={isText ? "text" : value && "column" in value ? value.column : ""}
          onChange={(e) => onChange(e.target.value === "text" ? { text: "" } : { column: Number(e.target.value) })}>
          <option value="" disabled>Choose…</option>
          {columns.map((c, i) => <option key={i} value={i}>Column: {c}</option>)}
          <option value="text">Same text for everyone…</option>
        </select>
        {isText && <input className="input" dir="auto" placeholder="Text" aria-label={`${label} text`}
          value={(value as { text: string }).text} onChange={(e) => onChange({ text: e.target.value })} />}
      </div>
    </div>
  );
}
