import { useEffect, useState } from "react";
import { get, type Activity } from "./api";
import { Banner, dateLabel } from "./ui";

const fieldLabels: Record<string, string> = {
  Name: "Name",
  OrganizationType: "Type",
  InternalReference: "Internal reference",
  StartsAtUtc: "Starts",
  EndsAtUtc: "Ends",
  TimeZoneId: "Time zone",
  LocationName: "Location",
};

function changes(record: Activity) {
  const before = JSON.parse(record.beforeJson) as Record<string, string | null>;
  const after = JSON.parse(record.afterJson) as Record<string, string | null>;
  return Object.entries(after)
    .filter(([field, value]) => before[field] !== value)
    .map(([field, value]) => {
      const format = (text: string | null | undefined) => {
        if (!text) return "Not set";
        return field === "StartsAtUtc" || field === "EndsAtUtc" ? dateLabel(text) : text;
      };
      return `${fieldLabels[field] ?? field}: ${format(before[field])} → ${format(value)}`;
    });
}

export function PlanningHistory({
  entityType,
  item,
  onClose,
}: {
  entityType: "Organization" | "Job" | "Event";
  item: { id: string; name: string; revision: number };
  onClose: () => void;
}) {
  const [records, setRecords] = useState<Activity[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    setRecords([]);
    get<Activity[]>(`/api/activity?entityType=${entityType}&entityId=${encodeURIComponent(item.id)}`)
      .then((result) => { if (active) setRecords(result); })
      .catch((cause: unknown) => {
        if (active) setError(cause instanceof Error ? cause.message : "Could not load change history.");
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [entityType, item.id, item.revision]);

  return (
    <section className="surface" aria-label={`Change history for ${item.name}`}>
      <div className="history-heading">
        <h2>Change history · {item.name}</h2>
        <button className="text-button" onClick={onClose}>Close history</button>
      </div>
      {loading && <p role="status">Loading changes…</p>}
      {error && <Banner tone="error">{error}</Banner>}
      {!loading && !error && records.length === 0 && <p className="surface-copy">No corrections recorded yet.</p>}
      {records.map((record) => (
        <article className="history-entry" key={record.id}>
          <strong>{dateLabel(record.occurredAtUtc)} · {record.actorEmail}</strong>
          <ul>{changes(record).map((change) => <li key={change}>{change}</li>)}</ul>
        </article>
      ))}
      {records.length === 100 && <p className="form-help">Showing the most recent 100 corrections.</p>}
    </section>
  );
}
